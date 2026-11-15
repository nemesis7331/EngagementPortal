using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Template email standard e risoluzione dell'ereditarietà
/// engagement → cliente → standard.
/// </summary>
public static class MailTemplates
{
    public const string StandardKey = "mail-templates";

    public static readonly IReadOnlyList<string> Placeholders =
    [
        "{{engagement}}",
        "{{code}}",
        "{{client}}",
        "{{owner}}",
        "{{start}}",
        "{{end}}",
        "{{reports}}",
        "{{date}}",
        "{{days}}",
        "{{task}}"
    ];

    /// <summary>
    /// Tipi di template gestiti: notifiche automatiche e solleciti. Una sola
    /// fonte per menu Amministrazione, endpoint e motore di invio.
    /// </summary>
    public static readonly IReadOnlyList<MailTemplateType> Types =
    [
        new("start", "Inizio attività", "Notifiche", "Distribution list notifiche",
            "All'avvio dell'attività"),
        new("end", "Fine attività", "Notifiche", "Distribution list notifiche",
            "Alla conclusione dell'attività"),
        new("deliver", "Consegna deliverables", "Notifiche",
            "Distribution list deliverables", "Alla pubblicazione dei report"),
        new("retestreport", "Invio report di retest", "Notifiche",
            "Distribution list deliverables",
            "All'invio del report di retest al cliente, senza revisione"),
        new("solrep", "Sollecito report al Pentester", "Solleciti", "Pentester",
            "3 giorni lavorativi dopo la fine pianificata, se i report non sono caricati"),
        new("solrev", "Sollecito revisione al Manager", "Solleciti", "Manager",
            "1 giorno da quando i deliverable arrivano in revisione"),
        new("solcred3", "Sollecito credenziali · 3 settimane", "Solleciti", "Cliente",
            "21 giorni prima dell'avvio, se le credenziali mancano"),
        new("solcred2", "Sollecito credenziali · 2 settimane", "Solleciti", "Cliente",
            "14 giorni prima dell'avvio, se le credenziali mancano"),
        new("solmanual", "Sollecito su richiesta", "Solleciti",
            "Chi ha l'azione in carico",
            "Quando un utente sollecita un'attesa dell'engagement")
    ];

    public static bool IsValidType(string? type) =>
        !string.IsNullOrWhiteSpace(type) &&
        Types.Any(item => item.Key == type);

    public static MailTemplateType? Type(string type) =>
        Types.FirstOrDefault(item => item.Key == type);

    public static MailTemplateSet Defaults() => new()
    {
        Start = new MailTemplate
        {
            Subject = "Avvio attività – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "l'attività {{engagement}} ({{code}}) per {{client}} è iniziata.\n" +
                "Periodo previsto: {{start}} – {{end}}.\n" +
                "Responsabile: {{owner}}.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Activity start – {{engagement}}",
            BodyEn =
                "Dear distribution list,\n\n" +
                "the activity {{engagement}} ({{code}}) for {{client}} has started.\n" +
                "Planned period: {{start}} – {{end}}.\n" +
                "Owner: {{owner}}.\n\n" +
                "This is an automated notification from the portal."
        },
        End = new MailTemplate
        {
            Subject = "Conclusione attività – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "l'attività {{engagement}} ({{code}}) per {{client}} si è conclusa il {{end}}.\n" +
                "Responsabile: {{owner}}.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Activity completion – {{engagement}}",
            BodyEn =
                "Dear distribution list,\n\n" +
                "the activity {{engagement}} ({{code}}) for {{client}} ended on {{end}}.\n" +
                "Owner: {{owner}}.\n\n" +
                "This is an automated notification from the portal."
        },
        Deliver = new MailTemplate
        {
            Subject = "Deliverables disponibili – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "i deliverables dell'attività {{engagement}} ({{code}}) per {{client}} sono disponibili.\n" +
                "Report pubblicati: {{reports}}.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Deliverables available – {{engagement}}",
            BodyEn =
                "Dear distribution list,\n\n" +
                "the deliverables for the activity {{engagement}} ({{code}}) for {{client}} are now available.\n" +
                "Published reports: {{reports}}.\n\n" +
                "This is an automated notification from the portal."
        },
        RetestReport = new MailTemplate
        {
            Subject = "Report di retest – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "il report del retest dell'attività {{engagement}} ({{code}}) per {{client}} " +
                "è disponibile: le verifiche dopo i fix sono state completate.\n" +
                "Data del report: {{date}}.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Retest report – {{engagement}}",
            BodyEn =
                "Dear distribution list,\n\n" +
                "the retest report for the activity {{engagement}} ({{code}}) for {{client}} " +
                "is available: the verification after the fixes is complete.\n" +
                "Report date: {{date}}.\n\n" +
                "This is an automated notification from the portal."
        },
        SolReportUpload = new MailTemplate
        {
            Subject = "Sollecito: caricamento report - {{engagement}}",
            Body =
                "Gentile {{owner}},\n\n" +
                "l'attività {{engagement}} ({{code}}) per {{client}} si è conclusa il {{end}} " +
                "e i report non risultano ancora caricati ({{days}} giorni lavorativi di ritardo).\n" +
                "Ti chiediamo di caricare i deliverable mancanti.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Reminder: report upload – {{engagement}}",
            BodyEn =
                "Dear {{owner}},\n\n" +
                "the activity {{engagement}} ({{code}}) for {{client}} ended on {{end}} " +
                "and the reports have not been uploaded yet ({{days}} working days late).\n" +
                "Please upload the missing deliverables.\n\n" +
                "This is an automated notification from the portal."
        },
        SolReview = new MailTemplate
        {
            Subject = "Sollecito: revisione deliverable – {{engagement}}",
            Body =
                "Gentile Manager,\n\n" +
                "i deliverable dell'attività {{engagement}} ({{code}}) per {{client}} " +
                "sono in revisione da {{days}} giorni.\n" +
                "Ti chiediamo di approvarli o richiedere le modifiche.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Reminder: deliverable review – {{engagement}}",
            BodyEn =
                "Dear Manager,\n\n" +
                "the deliverables for the activity {{engagement}} ({{code}}) for {{client}} " +
                "have been waiting for review for {{days}} days.\n" +
                "Please approve them or request changes.\n\n" +
                "This is an automated notification from the portal."
        },
        SolCredentialsEarly = new MailTemplate
        {
            Subject = "Sollecito: credenziali per {{engagement}}",
            Body =
                "Gentile {{client}},\n\n" +
                "l'attività {{engagement}} ({{code}}) è pianificata per il {{start}}: " +
                "mancano 3 settimane e le credenziali di accesso non risultano ancora disponibili.\n" +
                "Ti chiediamo di fornirle al più presto per non ritardare l'avvio.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Reminder: credentials for {{engagement}}",
            BodyEn =
                "Dear {{client}},\n\n" +
                "the activity {{engagement}} ({{code}}) is planned for {{start}}: " +
                "3 weeks are left and the access credentials are still missing.\n" +
                "Please provide them as soon as possible so the start is not delayed.\n\n" +
                "This is an automated notification from the portal."
        },
        SolCredentialsLate = new MailTemplate
        {
            Subject = "Sollecito: credenziali mancanti – {{engagement}}",
            Body =
                "Gentile {{client}},\n\n" +
                "l'avvio dell'attività {{engagement}} ({{code}}) è previsto per il {{start}} " +
                "e le credenziali di accesso non sono ancora state fornite.\n" +
                "Senza credenziali l'attività non può partire: ti chiediamo di inviarle subito.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Reminder: missing credentials – {{engagement}}",
            BodyEn =
                "Dear {{client}},\n\n" +
                "the activity {{engagement}} ({{code}}) is due to start on {{start}} " +
                "and the access credentials have not been provided yet.\n" +
                "Without credentials the activity cannot start: please send them now.\n\n" +
                "This is an automated notification from the portal."
        },
        SolManual = new MailTemplate
        {
            Subject = "Sollecito: {{task}} – {{engagement}}",
            Body =
                "Gentile {{owner}},\n\n" +
                "l'attività {{engagement}} ({{code}}) per {{client}} è in attesa di: {{task}}.\n" +
                "Ti chiediamo di completare l'azione quando puoi, così l'attività non resta ferma.\n\n" +
                "Questa è una comunicazione automatica del portale.",
            SubjectEn = "Reminder: {{task}} – {{engagement}}",
            BodyEn =
                "Dear {{owner}},\n\n" +
                "the activity {{engagement}} ({{code}}) for {{client}} is waiting for: {{task}}.\n" +
                "Please complete the action when you can, so the activity is not held up.\n\n" +
                "This is an automated notification from the portal."
        }
    };

    /// <summary>
    /// Unisce i default con gli eventuali override standard salvati, campo per
    /// campo e per lingua: un override solo italiano non perde l'inglese.
    /// </summary>
    public static MailTemplateSet Standard(MailTemplateSet? overrides)
    {
        var defaults = Defaults();
        var merged = new MailTemplateSet();

        foreach (var type in Types)
            merged.Set(type.Key, Merge(overrides?.For(type.Key), defaults.For(type.Key)!));

        return merged;
    }

    /// <summary>
    /// Scrive un template in un insieme di override (standard, cliente o
    /// engagement). Campi vuoti = ripristino: si torna a ereditare.
    /// </summary>
    public static (MailTemplateSet Set, bool Reset) Write(
        MailTemplateSet set,
        string type,
        string? subject,
        string? body,
        string? subjectEn,
        string? bodyEn,
        bool reset = false)
    {
        var cleanSubject = subject?.Trim() ?? string.Empty;
        var cleanBody = body?.Trim() ?? string.Empty;
        var cleanSubjectEn = subjectEn?.Trim() ?? string.Empty;
        var cleanBodyEn = bodyEn?.Trim() ?? string.Empty;

        var clear = reset ||
            (cleanSubject.Length == 0 &&
                cleanBody.Length == 0 &&
                cleanSubjectEn.Length == 0 &&
                cleanBodyEn.Length == 0);

        set.Set(type, clear
            ? null
            : new MailTemplate
            {
                Subject = cleanSubject,
                Body = cleanBody,
                SubjectEn = cleanSubjectEn,
                BodyEn = cleanBodyEn
            });

        return (set, clear);
    }

    private static MailTemplate Merge(
        MailTemplate? candidate,
        MailTemplate fallback) => new()
        {
            Subject = FirstNonEmpty(candidate?.Subject, fallback.Subject),
            Body = FirstNonEmpty(candidate?.Body, fallback.Body),
            SubjectEn = FirstNonEmpty(candidate?.SubjectEn, fallback.SubjectEn),
            BodyEn = FirstNonEmpty(candidate?.BodyEn, fallback.BodyEn)
        };

    private static string FirstNonEmpty(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;

    public static ResolvedMailTemplate Resolve(
        string type,
        Engagement engagement,
        MailTemplateSet standard,
        MailTemplateSet? client,
        DateOnly? today = null,
        string? language = null)
    {
        var pick = Pick(type, engagement, standard, client, language);

        var replacements = Replacements(
            engagement,
            today ?? DateOnly.FromDateTime(DateTime.UtcNow));

        return new ResolvedMailTemplate(
            Render(pick.Template.SubjectFor(pick.Language), replacements),
            Render(pick.Template.BodyFor(pick.Language), replacements),
            pick.Source,
            pick.Template);
    }

    /// <summary>
    /// Sceglie il template da usare e la lingua effettiva, senza rendere i
    /// segnaposto: serve a chi deve aggiungerne di propri (i solleciti).
    /// </summary>
    private static (MailTemplate Template, string Source, string Language) Pick(
        string type,
        Engagement engagement,
        MailTemplateSet standard,
        MailTemplateSet? client,
        string? language) =>
        PickFrom(
            engagement.MailTemplates.For(type),
            client?.For(type),
            standard,
            type,
            language,
            engagement.Language);

    /// <summary>Come <see cref="Pick"/> ma ignora l'override dell'engagement.</summary>
    public static ResolvedMailTemplate ResolveInherited(
        string type,
        Engagement engagement,
        MailTemplateSet standard,
        MailTemplateSet? client,
        DateOnly today)
    {
        var pick = PickFrom(
            null,
            client?.For(type),
            standard,
            type,
            null,
            engagement.Language);

        var replacements = Replacements(engagement, today);

        return new ResolvedMailTemplate(
            Render(pick.Template.SubjectFor(pick.Language), replacements),
            Render(pick.Template.BodyFor(pick.Language), replacements),
            pick.Source,
            pick.Template);
    }

    private static (MailTemplate Template, string Source, string Language) PickFrom(
        MailTemplate? engagementTemplate,
        MailTemplate? clientTemplate,
        MailTemplateSet standard,
        string type,
        string? language,
        string engagementLanguage)
    {
        var requested = language?.Trim().ToLowerInvariant();
        var effectiveLanguage = requested is "it" or "en"
            ? requested
            : engagementLanguage == "en" ? "en" : "it";

        if (engagementTemplate is { } fromEngagement &&
            fromEngagement.HasLanguage(effectiveLanguage))
            return (fromEngagement, "engagement", effectiveLanguage);

        if (clientTemplate is { } fromClient &&
            fromClient.HasLanguage(effectiveLanguage))
            return (fromClient, "client", effectiveLanguage);

        return (
            standard.For(type) ?? Defaults().For(type)!,
            "standard",
            effectiveLanguage);
    }

    public static string Render(
        string text,
        IReadOnlyDictionary<string, string> replacements)
    {
        var result = text ?? string.Empty;

        foreach (var (key, value) in replacements)
            result = result.Replace(key, value, StringComparison.Ordinal);

        return result;
    }

    /// <summary>
    /// Rende un template di sollecito, aggiungendo i segnaposto propri dei
    /// solleciti ({{days}}, {{task}}) a quelli dell'engagement.
    /// </summary>
    public static ResolvedMailTemplate RenderSolicitation(
        string type,
        Engagement engagement,
        MailTemplateSet standard,
        MailTemplateSet? client,
        DateOnly today,
        int days = 0,
        string? task = null)
    {
        var pick = Pick(type, engagement, standard, client, null);

        var replacements = new Dictionary<string, string>(
            Replacements(engagement, today),
            StringComparer.Ordinal)
        {
            ["{{days}}"] = days.ToString(),
            ["{{task}}"] = string.IsNullOrWhiteSpace(task)
                ? "completamento dell'azione richiesta"
                : task
        };

        return new ResolvedMailTemplate(
            Render(pick.Template.SubjectFor(pick.Language), replacements),
            Render(pick.Template.BodyFor(pick.Language), replacements),
            pick.Source,
            pick.Template);
    }

    public static IReadOnlyDictionary<string, string> Replacements(
        Engagement engagement,
        DateOnly today) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{engagement}}"] = engagement.Name,
            ["{{code}}"] = engagement.Id,
            ["{{client}}"] = engagement.ClientName,
            ["{{owner}}"] = engagement.Owner,
            ["{{start}}"] = engagement.AcceptedProposal is { } proposal
                ? proposal.StartDate.ToString("dd/MM/yyyy")
                : "da definire",
            ["{{end}}"] = engagement.AcceptedProposal is { } accepted
                ? accepted.EndDate.ToString("dd/MM/yyyy")
                : "da definire",
            ["{{reports}}"] = engagement.Reports
                .Count(report => report.Published)
                .ToString(),
            ["{{date}}"] = today.ToString("dd/MM/yyyy"),
            ["{{days}}"] = "0",
            ["{{task}}"] = "completamento dell'azione richiesta"
        };
}

public sealed record ResolvedMailTemplate(
    string Subject,
    string Body,
    string Source,
    MailTemplate Template);

public sealed record MailTemplateType(
    string Key,
    string Label,
    string Group,
    string Recipient,
    string When);
