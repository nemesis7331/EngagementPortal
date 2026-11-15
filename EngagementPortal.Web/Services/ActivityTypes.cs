using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>Tipo di attività con i suoi default.</summary>
public sealed record ActivityTypeDefinition(
    string Key,
    string Label,
    string Short,
    string Description,
    bool RetestPlanned,
    List<string> DeliverableTypes);

/// <summary>
/// Catalogo dei tipi di attività (VA, web pentest, red team…). Le chiavi sono
/// fisse perché legate all'engagement; etichette, descrizione, retest previsto
/// e deliverable di default si modificano da Amministrazione › Attività.
/// </summary>
public sealed class ActivityTypes(IAppSettingsRepository settings)
{
    public const string StorageKey = "activity-types";

    public static readonly IReadOnlyList<ActivityTypeDefinition> Defaults =
    [
        new(
            "PenetrationTest",
            "Penetration Test",
            "PT",
            "Attacco controllato su un perimetro concordato, con exploit e priorità di remediation.",
            true,
            [DeliverableKinds.Executive, DeliverableKinds.Technical, DeliverableKinds.Activity]),
        new(
            "VulnerabilityAssessment",
            "Vulnerability Assessment",
            "VA",
            "Scansione e analisi delle vulnerabilità senza sfruttamento delle stesse.",
            true,
            [DeliverableKinds.Executive, DeliverableKinds.Technical]),
        new(
            "RedTeam",
            "Red Team",
            "RT",
            "Simulazione adversaria prolungata: di norma non prevede il retest.",
            false,
            [DeliverableKinds.Executive, DeliverableKinds.Activity]),
        new(
            "CodeReview",
            "Code Review",
            "CR",
            "Revisione del codice sorgente, statica e manuale, con priorità dei difetti.",
            true,
            [DeliverableKinds.Technical]),
        new(
            "CloudSecurity",
            "Cloud Security",
            "CS",
            "Configurazioni, identità e permessi nel cloud, con verifica delle mitigazioni.",
            true,
            [DeliverableKinds.Executive, DeliverableKinds.Technical])
    ];

    public IReadOnlyList<ActivityTypeDefinition> All()
    {
        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];

        return Defaults
            .Select(standard =>
            {
                var custom = stored.FirstOrDefault(item =>
                    string.Equals(item.Key, standard.Key, StringComparison.OrdinalIgnoreCase));

                if (custom is null)
                    return standard;

                return standard with
                {
                    Label = Text(custom.Label, standard.Label),
                    Short = Text(custom.Short, standard.Short),
                    Description = Text(custom.Description, standard.Description),
                    RetestPlanned = custom.RetestPlanned,
                    DeliverableTypes = Canonical(
                        custom.DeliverableTypes,
                        standard.DeliverableTypes)
                };
            })
            .ToList();
    }

    public ActivityTypeDefinition? Get(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All().FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>True se il tipo è previsto dal catalogo.</summary>
    public bool IsValid(string? key) => Get(key) is not null;

    public bool Save(ActivityTypeDefinition definition, out string? error)
    {
        error = null;

        var standard = Defaults.FirstOrDefault(item =>
            string.Equals(item.Key, definition.Key, StringComparison.OrdinalIgnoreCase));

        if (standard is null)
        {
            error = "Tipo di attività non previsto.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Label))
        {
            error = "Serve un'etichetta per il tipo di attività.";
            return false;
        }

        var invalid = (definition.DeliverableTypes ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .FirstOrDefault(type => !DeliverableKinds.All.Contains(
                type, StringComparer.OrdinalIgnoreCase));

        if (invalid is not null)
        {
            error = $"Tipologia di deliverable non valida: {invalid}.";
            return false;
        }

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];
        stored.RemoveAll(item =>
            string.Equals(item.Key, standard.Key, StringComparison.OrdinalIgnoreCase));
        stored.Add(standard with
        {
            Label = definition.Label.Trim(),
            Short = Text(definition.Short, standard.Short),
            Description = Text(definition.Description, standard.Description),
            RetestPlanned = definition.RetestPlanned,
            DeliverableTypes = Canonical(
                definition.DeliverableTypes,
                standard.DeliverableTypes)
        });

        settings.Save(StorageKey, stored);
        return true;
    }

    /// <summary>Riporta un tipo ai valori di fabbrica.</summary>
    public bool Reset(string? key)
    {
        var standard = Defaults.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        if (standard is null)
            return false;

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];
        stored.RemoveAll(item =>
            string.Equals(item.Key, standard.Key, StringComparison.OrdinalIgnoreCase));
        settings.Save(StorageKey, stored);
        return true;
    }

    private static string Text(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>Ordina e valida i deliverable; vuoto = default del tipo.</summary>
    private static List<string> Canonical(
        IEnumerable<string>? requested,
        IReadOnlyList<string> fallback)
    {
        var clean = (requested ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (clean.Count == 0)
            return [.. fallback];

        return [.. DeliverableKinds.All.Where(kind =>
            clean.Contains(kind, StringComparer.OrdinalIgnoreCase))];
    }
}
