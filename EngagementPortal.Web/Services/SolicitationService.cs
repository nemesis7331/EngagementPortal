using System.Collections.Concurrent;
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
        IDateProvider clock,
        string? sentBy = null,
        string? sentByName = null) =>
        engagement.MailLog.Add(new MailLogEntry(
            $"M-{Guid.NewGuid():N}"[..10],
            SolicitationService.LogPrefix + taskKey,
            clock.UtcNow,
            string.Join("; ", recipients.Select(entry => entry.Email)),
            false,
            "All",
            template.Subject,
            template.Body,
            template.Source,
            sentBy,
            sentByName));
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

    private readonly ConcurrentDictionary<string, SolicitationRule> _rules = new(
        Rules.ToDictionary(rule => rule.Key, rule => rule));

    public IReadOnlyList<SolicitationRule> All() =>
        Rules.Select(rule => _rules[rule.Key]).ToList();

    /// <summary>Regola con le modifiche correnti (accensione, soglie).</summary>
    public SolicitationRule? Current(string key) =>
        _rules.TryGetValue(key, out var rule) ? rule : null;

    /// <summary>
    /// Aggiorna una regola: i campi null restano come sono. Le soglie fuori
    /// scala vengono ignorate invece di rompere il calcolo dei solleciti.
    /// </summary>
    public bool Update(
        string key,
        bool? enabled = null,
        int? thresholdDays = null,
        int? minDays = null,
        bool? countBusinessDays = null)
    {
        if (!_rules.TryGetValue(key, out var rule))
            return false;

        _rules[key] = rule with
        {
            Enabled = enabled ?? rule.Enabled,
            ThresholdDays = thresholdDays is >= 0 and <= 365
                ? thresholdDays.Value
                : rule.ThresholdDays,
            MinDays = minDays is >= 0 and <= 365
                ? minDays.Value
                : rule.MinDays,
            CountBusinessDays = countBusinessDays ?? rule.CountBusinessDays
        };

        return true;
    }

    /// <summary>Etichetta leggibile del task di un sollecito (per il registro).</summary>
    public static string TaskLabel(string taskKey) =>
        Rules.FirstOrDefault(rule => rule.TaskKey == taskKey)?.Label
        ?? (TaskLabels.TryGetValue(taskKey, out var label) ? label : taskKey);

    /// <summary>
    /// Ruolo che deve rispondere a un sollecito, a partire dall'etichetta della
    /// regola ("Cliente" è il ruolo <see cref="Roles.Client"/>).
    /// </summary>
    public static string RecipientRole(string recipient) =>
        recipient == "Cliente" ? Roles.Client : recipient;

    /// <summary>
    /// Task che non hanno una regola automatica ma possono generare un
    /// sollecito manuale dall'engagement.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> TaskLabels =
        new Dictionary<string, string>
        {
            ["setup"] = "Setup iniziale",
            ["manleva"] = "Firma della manleva",
            ["order"] = "Registrazione dell'ordine",
            ["financials"] = "Inserimento di costi e margine",
            ["financials-approval"] = "Approvazione di costi e margine",
            ["proposal"] = "Proposta delle date",
            ["proposal-approval"] = "Approvazione della proposta di date",
            ["scope"] = "Fornitura dello scope",
            ["credentials"] = "Fornitura delle credenziali",
            ["reports"] = "Caricamento dei deliverable",
            ["review"] = "Revisione dei deliverable",
            ["publish"] = "Pubblicazione dei deliverable"
        };

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

    /// <summary>
    /// Ultimo invio registrato per un task, in data locale. Con `senderId` si
    /// guarda solo ciò che ha inviato quella persona: il limite è per mittente.
    /// </summary>
    public static DateOnly? LastSent(
        Engagement engagement,
        string taskKey,
        string? senderId = null)
    {
        var entry = engagement.MailLog
            .Where(item => item.Type == LogPrefix + taskKey)
            .Where(item => senderId is null || item.SentBy == senderId)
            .OrderByDescending(item => item.Date)
            .FirstOrDefault();

        return entry is null
            ? null
            : DateOnly.FromDateTime(entry.Date.UtcDateTime);
    }

    public IReadOnlyList<SolicitationDue> Due(
        IEnumerable<Engagement> engagements,
        DateOnly today,
        string? senderId = null)
    {
        var result = new List<SolicitationDue>();

        foreach (var engagement in engagements)
        {
            var start = PlannedStart(engagement);
            var end = PlannedEnd(engagement);
            var daysToStart = start is { } startDate
                ? startDate.DayNumber - today.DayNumber
                : (int?)null;

            var reportRule = Current("rep")!;
            var reviewRule = Current("rev")!;
            var credentialsEarly = Current("cred3")!;
            var credentialsLate = Current("cred2")!;

            Add(
                "rep",
                days: end is { } endDate && !engagement.ReportsComplete
                    ? LateDays(reportRule, endDate, today)
                    : 0,
                applies: end is not null && !engagement.ReportsComplete,
                daysToStart: null);

            Add(
                "rev",
                days: engagement.ReviewStatus == ReviewStatus.Pending
                    ? LateDays(reviewRule, ReviewSince(engagement), today)
                    : 0,
                applies: engagement.ReviewStatus == ReviewStatus.Pending,
                daysToStart: null);

            // Due promemoria per le credenziali, in due finestre distinte: la
            // prima alla soglia della regola "cred3", la seconda alla soglia
            // "cred2" (14 giorni di default), entrambe modificabili.
            Add(
                "cred3",
                days: daysToStart is { } early ? credentialsEarly.ThresholdDays - early : 0,
                applies: !engagement.CredentialsDone &&
                    daysToStart is { } days3 &&
                    days3 > credentialsLate.ThresholdDays &&
                    days3 <= credentialsEarly.ThresholdDays,
                daysToStart: daysToStart);

            Add(
                "cred2",
                days: daysToStart is { } late ? credentialsLate.ThresholdDays - late : 0,
                applies: !engagement.CredentialsDone &&
                    daysToStart is { } days2 && days2 <= credentialsLate.ThresholdDays,
                daysToStart: daysToStart);

            void Add(string key, int days, bool applies, int? daysToStart)
            {
                var rule = Current(key)!;

                if (!applies || !rule.Enabled || days < rule.MinDays)
                    return;

                var sent = LastSent(engagement, rule.TaskKey, senderId);

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
    /// Con `senderId` l'ultimo invio è quello della persona: il limite è per
    /// mittente, non per task.
    /// </summary>
    public SolicitationTask? Pending(
        Engagement engagement,
        DateOnly today,
        string? senderId = null)
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

        var sent = LastSent(engagement, task.Key, senderId);

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
            ? DateOnly.FromDateTime(requested.UtcDateTime)
            : engagement.Reports.Count > 0
                ? DateOnly.FromDateTime(
                    engagement.Reports.Max(report => report.Date).UtcDateTime)
                : DateOnly.FromDateTime(engagement.CreatedDate.UtcDateTime);

    private static int DaysSince(DateOnly since, DateOnly today) =>
        Math.Max(0, today.DayNumber - since.DayNumber);

    /// <summary>
    /// Giorni oltre la soglia: lavorativi o di calendario secondo la regola,
    /// così il flag <see cref="SolicitationRule.CountBusinessDays"/> comanda.
    /// </summary>
    private static int LateDays(
        SolicitationRule rule,
        DateOnly from,
        DateOnly today) =>
        rule.CountBusinessDays
            ? BusinessDays(from.AddDays(1), today)
            : DaysSince(from, today);
}
