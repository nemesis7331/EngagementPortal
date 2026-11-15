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
/// Catalogo dei tipi di attività (VA, web pentest, red team…). I tipi standard
/// hanno chiavi fisse perché legate all'engagement; etichette, descrizione,
/// retest previsto e deliverable di default si modificano da Amministrazione ›
/// Tipi di attività. Da lì si possono anche creare tipi nuovi, che restano nel
/// catalogo con una chiave generata dall'etichetta.
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
        var result = new List<ActivityTypeDefinition>();

        foreach (var standard in Defaults)
        {
            var custom = stored.FirstOrDefault(item => Same(item.Key, standard.Key));

            result.Add(custom is null
                ? standard
                : standard with
                {
                    Label = Text(custom.Label, standard.Label),
                    Short = Text(custom.Short, standard.Short),
                    Description = Text(custom.Description, standard.Description),
                    RetestPlanned = custom.RetestPlanned,
                    DeliverableTypes = Canonical(
                        custom.DeliverableTypes,
                        standard.DeliverableTypes)
                });
        }

        // Tipi creati da Amministrazione: vengono dopo gli standard, nell'ordine
        // in cui sono stati aggiunti.
        foreach (var custom in stored)
        {
            if (string.IsNullOrWhiteSpace(custom.Key) ||
                string.IsNullOrWhiteSpace(custom.Label) ||
                result.Any(item => Same(item.Key, custom.Key)))
            {
                continue;
            }

            var label = custom.Label.Trim();

            result.Add(new ActivityTypeDefinition(
                custom.Key.Trim(),
                label,
                Text(custom.Short, label[..1].ToUpperInvariant()),
                custom.Description?.Trim() ?? string.Empty,
                custom.RetestPlanned,
                Canonical(custom.DeliverableTypes, [DeliverableKinds.Technical])));
        }

        return result;
    }

    public ActivityTypeDefinition? Get(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All().FirstOrDefault(item => Same(item.Key, key));

    /// <summary>True se il tipo è previsto dal catalogo.</summary>
    public bool IsValid(string? key) => Get(key) is not null;

    /// <summary>Crea un tipo nuovo e restituisce la definizione salvata.</summary>
    public ActivityTypeDefinition? Create(
        string? label,
        string? shortName,
        string? description,
        bool retestPlanned,
        IEnumerable<string>? deliverableTypes,
        out string? error)
    {
        error = null;
        var cleanLabel = label?.Trim() ?? string.Empty;

        if (cleanLabel.Length < 2)
        {
            error = "Serve un'etichetta di almeno due caratteri.";
            return null;
        }

        var cleanShort = string.IsNullOrWhiteSpace(shortName)
            ? new string(cleanLabel.Where(char.IsLetterOrDigit).Take(2).ToArray())
                .ToUpperInvariant()
            : shortName.Trim();

        if (cleanShort.Length > 4)
        {
            error = "La sigla può avere al massimo 4 caratteri.";
            return null;
        }

        if (!ValidateDeliverables(deliverableTypes, out error))
            return null;

        var definition = new ActivityTypeDefinition(
            UniqueKey(cleanLabel),
            cleanLabel,
            cleanShort,
            description?.Trim() ?? string.Empty,
            retestPlanned,
            Canonical(deliverableTypes, [DeliverableKinds.Technical]));

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];
        stored.Add(definition);
        settings.Save(StorageKey, stored);

        return definition;
    }

    public bool Save(ActivityTypeDefinition definition, out string? error)
    {
        error = null;

        var existing = Get(definition.Key);

        if (existing is null)
        {
            error = "Tipo di attività non previsto.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Label))
        {
            error = "Serve un'etichetta per il tipo di attività.";
            return false;
        }

        var shortName = Text(definition.Short, existing.Short);

        if (shortName.Length > 4)
        {
            error = "La sigla può avere al massimo 4 caratteri.";
            return false;
        }

        if (!ValidateDeliverables(definition.DeliverableTypes, out error))
            return false;

        var fallback = Defaults
            .FirstOrDefault(item => Same(item.Key, existing.Key))
            ?.DeliverableTypes ?? [DeliverableKinds.Technical];

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];
        stored.RemoveAll(item => Same(item.Key, existing.Key));
        stored.Add(existing with
        {
            Label = definition.Label.Trim(),
            Short = shortName,
            Description = Text(definition.Description, existing.Description),
            RetestPlanned = definition.RetestPlanned,
            DeliverableTypes = Canonical(definition.DeliverableTypes, fallback)
        });

        settings.Save(StorageKey, stored);
        return true;
    }

    /// <summary>Riporta un tipo standard ai valori di fabbrica.</summary>
    public bool Reset(string? key)
    {
        var standard = Defaults.FirstOrDefault(item => Same(item.Key, key));

        if (standard is null)
            return false;

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];
        stored.RemoveAll(item => Same(item.Key, standard.Key));
        settings.Save(StorageKey, stored);
        return true;
    }

    /// <summary>Elimina un tipo creato da Amministrazione (non gli standard).</summary>
    public bool Delete(string? key, out string? error)
    {
        error = null;

        var existing = Get(key);

        if (existing is null)
        {
            error = "Tipo di attività non trovato.";
            return false;
        }

        if (Defaults.Any(item => Same(item.Key, existing.Key)))
        {
            error = "I tipi standard non si eliminano: puoi ripristinarli.";
            return false;
        }

        var stored = settings.Get<List<ActivityTypeDefinition>>(StorageKey) ?? [];

        if (stored.RemoveAll(item => Same(item.Key, existing.Key)) == 0)
        {
            error = "Tipo di attività non trovato.";
            return false;
        }

        settings.Save(StorageKey, stored);
        return true;
    }

    private string UniqueKey(string label)
    {
        var baseKey = string.Concat(label
            .Split([' ', '-', '_', '.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

        if (baseKey.Length == 0)
            baseKey = "Tipo";

        var candidate = baseKey;

        for (var suffix = 2; All().Any(item => Same(item.Key, candidate)); suffix += 1)
            candidate = baseKey + suffix;

        return candidate;
    }

    private static bool ValidateDeliverables(
        IEnumerable<string>? types,
        out string? error)
    {
        var invalid = (types ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .FirstOrDefault(type => !DeliverableKinds.All.Contains(
                type, StringComparer.OrdinalIgnoreCase));

        error = invalid is null
            ? null
            : $"Tipologia di deliverable non valida: {invalid}.";

        return invalid is null;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);

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
