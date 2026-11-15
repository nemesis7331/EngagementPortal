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
        "{{date}}"
    ];

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
        }
    };

    /// <summary>
    /// Unisce i default con gli eventuali override standard salvati, campo per
    /// campo e per lingua: un override solo italiano non perde l'inglese.
    /// </summary>
    public static MailTemplateSet Standard(MailTemplateSet? overrides)
    {
        var defaults = Defaults();

        return new MailTemplateSet
        {
            Start = Merge(overrides?.Start, defaults.Start!),
            End = Merge(overrides?.End, defaults.End!),
            Deliver = Merge(overrides?.Deliver, defaults.Deliver!)
        };
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
        var requested = language?.Trim().ToLowerInvariant();
        var effectiveLanguage = requested is "it" or "en"
            ? requested
            : engagement.Language == "en" ? "en" : "it";

        var engagementTemplate = engagement.MailTemplates.For(type);
        var clientTemplate = client?.For(type);

        MailTemplate template;
        string source;

        if (engagementTemplate is { } fromEngagement &&
            fromEngagement.HasLanguage(effectiveLanguage))
        {
            template = fromEngagement;
            source = "engagement";
        }
        else if (clientTemplate is { } fromClient &&
            fromClient.HasLanguage(effectiveLanguage))
        {
            template = fromClient;
            source = "client";
        }
        else
        {
            template = standard.For(type) ?? Defaults().For(type)!;
            source = "standard";
        }

        var replacements = Replacements(
            engagement,
            today ?? DateOnly.FromDateTime(DateTime.UtcNow));

        return new ResolvedMailTemplate(
            Render(template.SubjectFor(effectiveLanguage), replacements),
            Render(template.BodyFor(effectiveLanguage), replacements),
            source,
            template);
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
            ["{{date}}"] = today.ToString("dd/MM/yyyy")
        };
}

public sealed record ResolvedMailTemplate(
    string Subject,
    string Body,
    string Source,
    MailTemplate Template);
