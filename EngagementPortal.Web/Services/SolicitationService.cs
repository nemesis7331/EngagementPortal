using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Regola di sollecito automatico. Le regole sono fisse: cambiano solo il
/// testo (in Amministrazione › Template mail) e l'accensione.
/// </summary>
public sealed record SolicitationRule(
    string Key,
    string TemplateType,
    string TaskKey,
    string Label,
    string Recipient,
    string When,
    int ThresholdDays,
    int MinDays,
    bool CountBusinessDays,
    bool Enabled,
    string Description);

/// <summary>Un sollecito dovuto per un engagement.</summary>
public sealed record SolicitationDue(
    string Key,
    string RuleKey,
    string TaskKey,
    string Label,
    string EngagementId,
    string ClientName,
    string Recipient,
    string When,
    int ThresholdDays,
    int Days,
    int? DaysToStart,
    string SentDate,
    string SentOn,
    bool SentToday);

/// <summary>L'attesa in corso su un engagement: base del sollecito manuale.</summary>
public sealed record SolicitationTask(
    string Key,
    string Label,
    string Recipient,
    IReadOnlyList<string> OwnerRoles,
    string SentDate,
    bool SentToday);

/// <summary>
/// Destinatari e registrazione di un sollecito. Un sollecito al cliente va
/// alla distribution list notifiche; uno interno ai colleghi con quel ruolo.
/// </summary>
public static class Solicitation
{
    public static IReadOnlyList<DistributionEntry> Recipients(
        Engagement engagement,
        IReadOnlyCollection<string> ownerRoles,
        ClientAccount? client,
        IReadOnlyList<UserAccount> users)
    {
        var notification = Distribution.Effective(
            engagement,
            client?.DistributionList ?? []);

        if (ownerRoles.Contains(Roles.Client) && notification.Count > 0)
            return notification;

        var accounts = users
            .Where(user => user.Active && ownerRoles.Contains(user.Role))
            .Select(user => new DistributionEntry(
                user.Id,
                user.Name,
                user.Email,
                "Provider"))
            .ToList();

        return accounts.Count > 0 ? accounts : notification;
    }

    public static void Record(
        Engagement engagement,
        string taskKey,
        ResolvedMailTemplate template,
        IReadOnlyList<DistributionEntry> recipients,
        IDateProvider clock) =>
        engagement.MailLog.Add(new MailLogEntry(
            $"M-{Guid.NewGuid():N}"[..10],
            SolicitationService.LogPrefix + taskKey,
            clock.UtcNow,
            string.Join("; ", recipients.Select(entry => entry.Email)),
            false,
            "All",
            template.Subject,
            template.Body,
            template.Source));
}

/// <summary>
/// Solleciti automatici. Il testo non vive qui: viene risolto dai template
/// (standard → cliente → engagement) con <see cref="MailTemplates"/>.
/// </summary>
public sealed class SolicitationService(EngagementWorkflow workflow)
{
    /// <summary>Prefisso delle voci di registro: un invio al giorno per task.</summary>
    public const string LogPrefix = "solicitation:";

    public static readonly IReadOnlyList<SolicitationRule> Rules =
    [
        new(
            "rep",
            "solrep",
            "reports",
            "Caricamento report",
            "Pentester",
            "3 giorni lavorativi dopo la fine pianificata, se i report non sono caricati",
            3,
            3,
            true,
            true,
            "Il Pentester non ha ancora caricato i deliverable dopo la fine dell'attività."),
        new(
            "rev",
            "solrev",
            "review",
            "Revisione deliverable",
            "Manager",
            "1 giorno da quando i deliverable arrivano in revisione",
            1,
            1,
            false,
            true,
            "I deliverable sono in revisione e il Manager non ha ancora deciso."),
        new(
            "cred3",
            "solcred3",
            "credentials",
            "Credenziali · 3 settimane all'avvio",
            "Cliente",
            "21 giorni prima dell'avvio, se le credenziali mancano",
            21,
            0,
            false,
            true,
            "Primo promemoria al Cliente per fornire le credenziali."),
        new(
            "cred2",
            "solcred2",
            "credentials",
            "Credenziali · 2 settimane all'avvio",
            "Cliente",
            "14 giorni prima dell'avvio, se le credenziali mancano",
            14,
            0,
            false,
            true,
            "Promemoria ravvicinato: senza credenziali l'attività non parte.")
    ];

    private readonly Dictionary<string, bool> _enabled =
        Rules.ToDictionary(rule => rule.Key, rule => rule.Enabled);

    public IReadOnlyList<SolicitationRule> All() =>
        Rules.Select(rule => rule with { Enabled = _enabled[rule.Key] }).ToList();

    public bool Update(string key, bool enabled)
    {
        if (!Rules.Any(rule => rule.Key == key))
            return false;

        _enabled[key] = enabled;
        return true;
    }

    public static SolicitationRule? Rule(string key) =>
        Rules.FirstOrDefault(rule => rule.Key == key);

    /// <summary>Giorni lavorativi (lun-ven) fra due date, estremi inclusi a destra.</summary>
    public static int BusinessDays(DateOnly from, DateOnly to)
    {
        if (to < from)
            return 0;

        var count = 0;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        }

        return count;
    }

    /// <summary>Data di riferimento per l'avvio: proposta accettata o prima pendente.</summary>
    public static DateOnly? PlannedStart(Engagement engagement) =>
        engagement.AcceptedProposal?.StartDate ??
        engagement.Proposals
            .Where(proposal => proposal.Status == ProposalStatus.Pending)
            .OrderBy(proposal => proposal.StartDate)
            .Select(proposal => (DateOnly?)proposal.StartDate)
            .FirstOrDefault();

    public static DateOnly? PlannedEnd(Engagement engagement) =>
        engagement.AcceptedProposal?.EndDate;

    /// <summary>Ultimo invio registrato per un task, in data locale.</summary>
    public static DateOnly? LastSent(Engagement engagement, string taskKey)
    {
        var entry = engagement.MailLog
            .Where(item => item.Type == LogPrefix + taskKey)
            .OrderByDescending(item => item.Date)
            .FirstOrDefault();

        return entry is null
            ? null
            : DateOnly.FromDateTime(entry.Date.LocalDateTime);
    }

    public IReadOnlyList<SolicitationDue> Due(
        IEnumerable<Engagement> engagements,
        DateOnly today)
    {
        var result = new List<SolicitationDue>();

        foreach (var engagement in engagements)
        {
            var start = PlannedStart(engagement);
            var end = PlannedEnd(engagement);
            var daysToStart = start is { } startDate
                ? startDate.DayNumber - today.DayNumber
                : (int?)null;

            Add(
                "rep",
                days: end is { } endDate && !engagement.ReportsComplete
                    ? BusinessDays(endDate.AddDays(1), today)
                    : 0,
                applies: end is not null && !engagement.ReportsComplete,
                daysToStart: null);

            Add(
                "rev",
                days: engagement.ReviewStatus == ReviewStatus.Pending
                    ? DaysSince(ReviewSince(engagement), today)
                    : 0,
                applies: engagement.ReviewStatus == ReviewStatus.Pending,
                daysToStart: null);

            // Due promemoria per le credenziali, in due finestre distinte:
            // il primo da 3 settimane, il secondo da 2 settimane all'avvio.
            Add(
                "cred3",
                days: daysToStart is { } early ? 21 - early : 0,
                applies: !engagement.CredentialsDone &&
                    daysToStart is { } days3 && days3 is > 14 and <= 21,
                daysToStart: daysToStart);

            Add(
                "cred2",
                days: daysToStart is { } late ? 14 - late : 0,
                applies: !engagement.CredentialsDone &&
                    daysToStart is { } days2 && days2 <= 14,
                daysToStart: daysToStart);

            void Add(string key, int days, bool applies, int? daysToStart)
            {
                var rule = Rule(key)!;

                if (!applies || !_enabled[key] || days < rule.MinDays)
                    return;

                var sent = LastSent(engagement, rule.TaskKey);

                result.Add(new SolicitationDue(
                    $"{engagement.Id}:{key}",
                    key,
                    rule.TaskKey,
                    rule.Label,
                    engagement.Id,
                    engagement.ClientName,
                    rule.Recipient,
                    rule.When,
                    rule.ThresholdDays,
                    days,
                    daysToStart,
                    sent is null ? string.Empty : sent.Value.ToString("dd/MM/yyyy"),
                    sent is null ? string.Empty : sent.Value.ToString("yyyy-MM-dd"),
                    sent == today));
            }
        }

        return result
            .OrderByDescending(item => item.Days)
            .ThenBy(item => item.EngagementId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Attesa in corso: chi deve agire adesso e quindi chi si può sollecitare.
    /// Null quando non c'è nulla in sospeso (pianificato, in corso, concluso).
    /// </summary>
    public SolicitationTask? Pending(Engagement engagement, DateOnly today)
    {
        var phase = workflow.Resolve(engagement, today);

        var task = phase switch
        {
            EngagementPhase.Setup =>
                new SolicitationTask(
                    "setup",
                    "definizione del setup iniziale",
                    "il PMO",
                    [Roles.PMO, Roles.Manager],
                    string.Empty,
                    false),
            EngagementPhase.Docs => DocsTask(engagement),
            EngagementPhase.Planning => PlanningTask(engagement),
            EngagementPhase.Scope =>
                new SolicitationTask(
                    "scope",
                    "fornitura dello scope",
                    "il cliente",
                    [Roles.Client],
                    string.Empty,
                    false),
            EngagementPhase.Credentials =>
                new SolicitationTask(
                    "credentials",
                    "fornitura delle credenziali di accesso",
                    "il cliente",
                    [Roles.Client],
                    string.Empty,
                    false),
            EngagementPhase.Reporting => ReportingTask(engagement),
            _ => null
        };

        if (task is null)
            return null;

        var sent = LastSent(engagement, task.Key);

        return task with
        {
            SentDate = sent is null ? string.Empty : sent.Value.ToString("dd/MM/yyyy"),
            SentToday = sent == today
        };
    }

    private static SolicitationTask DocsTask(Engagement engagement) =>
        !engagement.HasManleva
            ? new SolicitationTask(
                "manleva",
                "firma della manleva",
                "il cliente",
                [Roles.Client],
                string.Empty,
                false)
            : !engagement.OrderConfirmed
                ? new SolicitationTask(
                    "order",
                    "registrazione dell'ordine",
                    "il PMO",
                    [Roles.PMO],
                    string.Empty,
                    false)
                : !engagement.FinancialsDone
                    ? new SolicitationTask(
                        "financials",
                        "inserimento di costi e margine",
                        "il PMO o il Manager",
                        [Roles.PMO, Roles.Manager],
                        string.Empty,
                        false)
                    : new SolicitationTask(
                        "financials-approval",
                        "approvazione di costi e margine",
                        "il Manager",
                        [Roles.Manager],
                        string.Empty,
                        false);

    private static SolicitationTask PlanningTask(Engagement engagement)
    {
        var pending = engagement.Proposals
            .FirstOrDefault(proposal => proposal.Status == ProposalStatus.Pending);

        return pending is not null
            ? new SolicitationTask(
                "proposal-approval",
                "approvazione della proposta di date",
                "il Manager",
                [Roles.Manager],
                string.Empty,
                false)
            : new SolicitationTask(
                "proposal",
                "proposta delle date",
                "il PMO o il cliente",
                [Roles.PMO, Roles.Client],
                string.Empty,
                false);
    }

    private static SolicitationTask ReportingTask(Engagement engagement) =>
        engagement.ReviewStatus switch
        {
            ReviewStatus.Pending => new SolicitationTask(
                "review",
                "revisione dei deliverable",
                "il Manager",
                [Roles.Manager],
                string.Empty,
                false),
            ReviewStatus.Approved => new SolicitationTask(
                "publish",
                "pubblicazione dei deliverable",
                "il Manager",
                [Roles.Manager],
                string.Empty,
                false),
            _ => new SolicitationTask(
                "reports",
                "caricamento dei deliverable",
                "il Pentester",
                [Roles.Pentester],
                string.Empty,
                false)
        };

    private static DateOnly ReviewSince(Engagement engagement) =>
        engagement.ReviewRequestedDate is { } requested
            ? DateOnly.FromDateTime(requested.LocalDateTime)
            : engagement.Reports.Count > 0
                ? DateOnly.FromDateTime(
                    engagement.Reports.Max(report => report.Date).LocalDateTime)
                : DateOnly.FromDateTime(engagement.CreatedDate.LocalDateTime);

    private static int DaysSince(DateOnly since, DateOnly today) =>
        Math.Max(0, today.DayNumber - since.DayNumber);
}
