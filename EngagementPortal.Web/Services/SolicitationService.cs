using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public sealed record SolicitationRule(
    string Key,
    string Label,
    string Subject,
    bool Enabled,
    int ThresholdDays,
    string Description);

public sealed record SolicitationDue(
    string Key,
    string Label,
    string EngagementId,
    string ClientName,
    int Days,
    int ThresholdDays);

public sealed class SolicitationService(EngagementWorkflow workflow)
{
    private static readonly Dictionary<string, (string Label, string Subject, int Threshold, string Description)> Defaults = new()
    {
        ["manleva"] = ("Manleva mancante", "Sollecito manleva", 3, "Manleva non ancora caricata dal cliente"),
        ["order"] = ("Ordine mancante", "Sollecito ordine", 5, "Ordine non ancora registrato"),
        ["prop"] = ("Pianificazione ferma", "Sollecito pianificazione", 2, "Nessuna proposta di date accettata"),
        ["prep"] = ("Preparazione incompleta", "Sollecito preparazione", 7, "Scope o credenziali mancanti"),
        ["rep"] = ("Deliverable mancanti", "Sollecito report", 2, "Nessun deliverable pubblicato a fine engagement"),
        ["rev"] = ("Revisione in attesa", "Sollecito revisione", 1, "Deliverable in attesa di revisione manager")
    };

    private readonly Dictionary<string, bool> _enabled =
        Defaults.Keys.ToDictionary(key => key, _ => true);

    private readonly Dictionary<string, int> _thresholds =
        Defaults.ToDictionary(entry => entry.Key, entry => entry.Value.Threshold);

    public IReadOnlyList<SolicitationRule> Rules() =>
        Defaults.Select(entry => new SolicitationRule(
            entry.Key,
            entry.Value.Label,
            entry.Value.Subject,
            _enabled[entry.Key],
            _thresholds[entry.Key],
            entry.Value.Description)).ToList();

    public bool Update(string key, bool enabled, int thresholdDays)
    {
        if (!Defaults.ContainsKey(key))
            return false;

        _enabled[key] = enabled;
        _thresholds[key] = Math.Clamp(thresholdDays, 0, 365);
        return true;
    }

    public IReadOnlyList<SolicitationDue> Due(
        IEnumerable<Engagement> engagements,
        DateOnly today)
    {
        var result = new List<SolicitationDue>();

        foreach (var engagement in engagements)
        {
            var phase = workflow.Resolve(engagement, today);
            var age = Math.Max(
                0,
                today.DayNumber -
                DateOnly.FromDateTime(engagement.CreatedDate.UtcDateTime).DayNumber);

            var lastProposal = engagement.Proposals
                .OrderByDescending(proposal => proposal.StartDate)
                .FirstOrDefault();

            var proposalAge = lastProposal is null
                ? age
                : Math.Max(0, today.DayNumber - lastProposal.StartDate.DayNumber);

            var lastReport = engagement.Reports
                .OrderByDescending(report => report.Date)
                .FirstOrDefault();

            var reportAge = lastReport is null
                ? age
                : Math.Max(0, (DateTimeOffset.UtcNow - lastReport.Date).Days);

            Add("manleva", age, phase == EngagementPhase.Docs && !engagement.HasManleva);
            Add("order", age, phase == EngagementPhase.Docs && !engagement.OrderConfirmed);
            Add("prop", proposalAge, phase == EngagementPhase.Planning);
            Add("prep", age,
                phase is EngagementPhase.Scope or
                    EngagementPhase.Credentials);
            Add("rep", reportAge,
                phase == EngagementPhase.Reporting &&
                !engagement.Reports.Any(report => report.Published));
            Add("rev", reportAge,
                engagement.ReviewStatus == ReviewStatus.Pending);

            void Add(string key, int days, bool applies)
            {
                if (!applies || !_enabled[key] || days < _thresholds[key])
                    return;

                result.Add(new SolicitationDue(
                    key,
                    Defaults[key].Label,
                    engagement.Id,
                    engagement.ClientName,
                    days,
                    _thresholds[key]));
            }
        }

        return result
            .OrderByDescending(item => item.Days)
            .ToList();
    }
}
