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
                "Questa è una comunicazione automatica del portale."
        },
        End = new MailTemplate
        {
            Subject = "Conclusione attività – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "l'attività {{engagement}} ({{code}}) per {{client}} si è conclusa il {{end}}.\n" +
                "Responsabile: {{owner}}.\n\n" +
                "Questa è una comunicazione automatica del portale."
        },
        Deliver = new MailTemplate
        {
            Subject = "Deliverables disponibili – {{engagement}}",
            Body =
                "Gentile distribution list,\n\n" +
                "i deliverables dell'attività {{engagement}} ({{code}}) per {{client}} sono disponibili.\n" +
                "Report pubblicati: {{reports}}.\n\n" +
                "Questa è una comunicazione automatica del portale."
        }
    };

    /// <summary>
    /// Unisce i default con gli eventuali override standard salvati.
    /// </summary>
    public static MailTemplateSet Standard(MailTemplateSet? overrides)
    {
        var defaults = Defaults();

        return new MailTemplateSet
        {
            Start = Pick(overrides?.Start, defaults.Start!),
            End = Pick(overrides?.End, defaults.End!),
            Deliver = Pick(overrides?.Deliver, defaults.Deliver!)
        };
    }

    private static MailTemplate Pick(
        MailTemplate? candidate,
        MailTemplate fallback) =>
        candidate is { IsBlank: false } ? candidate : fallback;

    public static ResolvedMailTemplate Resolve(
        string type,
        Engagement engagement,
        MailTemplateSet standard,
        MailTemplateSet? client,
        DateOnly? today = null)
    {
        var engagementTemplate = engagement.MailTemplates.For(type);
        var clientTemplate = client?.For(type);

        MailTemplate template;
        string source;

        if (engagementTemplate is { IsBlank: false })
        {
            template = engagementTemplate;
            source = "engagement";
        }
        else if (clientTemplate is { IsBlank: false })
        {
            template = clientTemplate;
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
            Render(template.Subject, replacements),
            Render(template.Body, replacements),
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
