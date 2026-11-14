using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Simula l'invio automatico delle email di inizio, fine e consegna:
/// crea la voce nel registro con oggetto e corpo renderizzati, senza
/// contattare alcun server SMTP. Il servizio gira all'avvio e poi a
/// intervalli regolari.
/// </summary>
public sealed class MailAutomationService(
    IServiceScopeFactory scopeFactory,
    IDateProvider clock,
    EngagementWorkflow workflow,
    ILogger<MailAutomationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lascia completare l'avvio dell'app.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ProcessDue();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Errore durante l'elaborazione delle email automatiche.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    public void ProcessDue()
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IEngagementRepository>();
        var clients = scope.ServiceProvider
            .GetRequiredService<IClientRepository>();
        var settings = scope.ServiceProvider
            .GetRequiredService<IAppSettingsRepository>();

        var standard = MailTemplates.Standard(
            settings.Get<MailTemplateSet>(MailTemplates.StandardKey));

        var prepared = 0;

        foreach (var engagement in repository.List(
            string.Empty,
            allClients: true))
        {
            var client = clients.Get(engagement.ClientId);
            var notificationRecipients = Distribution.Effective(
                engagement,
                client?.DistributionList ?? []);
            var deliverableRecipients =
                Distribution.EffectiveDeliverables(
                    engagement,
                    client?.DeliverablesDistributionList ?? [],
                    client?.DistributionList ?? []);

            if (notificationRecipients.Count == 0 &&
                deliverableRecipients.Count == 0)
                continue;

            var phase = workflow.Resolve(engagement, clock.Today);

            // L'email di inizio parte solo quando l'attività è arrivata in
            // esecuzione (o è già in report); quella di fine quando si entra
            // in report o si chiude. Un engagement fermo nei passi precedenti
            // non deve ricevere email di avvio solo perché le date sono passate.
            var startDue =
                engagement.Mail.Start &&
                engagement.AcceptedProposal is not null &&
                clock.Today >= engagement.AcceptedProposal.StartDate &&
                phase is EngagementPhase.Running or EngagementPhase.Reporting;

            var endDue =
                engagement.Mail.End &&
                engagement.AcceptedProposal is not null &&
                clock.Today > engagement.AcceptedProposal.EndDate &&
                phase is EngagementPhase.Reporting or EngagementPhase.Done;

            var preparedHere =
                TryPrepare(
                    engagement,
                    "start",
                    startDue,
                    standard,
                    client,
                    notificationRecipients) +
                TryPrepare(
                    engagement,
                    "end",
                    endDue,
                    standard,
                    client,
                    notificationRecipients) +
                TryPrepare(
                    engagement,
                    "deliver",
                    engagement.Mail.Deliver &&
                        engagement.Reports.Any(report => report.Published),
                    standard,
                    client,
                    deliverableRecipients);

            if (preparedHere > 0)
            {
                prepared += preparedHere;
                repository.Save(engagement);
            }
        }

        if (prepared > 0)
        {
            logger.LogInformation(
                "Email automatiche simulate: {Count} (nessun invio SMTP).",
                prepared);
        }
    }

    private int TryPrepare(
        Engagement engagement,
        string type,
        bool due,
        MailTemplateSet standard,
        ClientAccount? client,
        IReadOnlyList<DistributionEntry> recipients)
    {
        if (!due)
            return 0;

        if (recipients.Count == 0)
            return 0;

        if (engagement.MailLog.Any(entry => entry.Type == type))
            return 0;

        var resolved = MailTemplates.Resolve(
            type,
            engagement,
            standard,
            client?.MailTemplates,
            clock.Today);

        engagement.MailLog.Add(new MailLogEntry(
            $"M-{Guid.NewGuid():N}"[..10],
            type,
            clock.UtcNow,
            string.Join("; ", recipients.Select(entry => entry.Email)),
            true,
            "All",
            resolved.Subject,
            resolved.Body,
            resolved.Source));

        engagement.AddAudit(
            "Sistema",
            Roles.PMO,
            $"Email automatica '{type}' simulata",
            AuditVisibility.ProviderOnly,
            "Email automatica preparata");

        return 1;
    }
}
