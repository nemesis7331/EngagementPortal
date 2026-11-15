using System.Text.Json.Serialization;

namespace EngagementPortal.Web.Domain;

public enum EngagementType
{
    PenetrationTest,
    VulnerabilityAssessment,
    RedTeam,
    CodeReview,
    CloudSecurity,
    // Tipo creato da Amministrazione › Tipi di attività: la chiave vera sta
    // in Engagement.ActivityTypeKey e il catalogo la risolve.
    Custom
}

public enum EngagementPhase
{
    Setup,       // 0. Setup: team, liste, lingua e deliverable
    Docs,        // 1. Ordine e manleva
    Scope,       // 2. Scope
    Planning,    // 3. Pianificazione
    Credentials, // 4. Credenziali
    Scheduled,   // 5. Esecuzione (prima dell'avvio)
    Running,     // 5. Esecuzione (in corso)
    Reporting,   // 6. Report
    Retest,          // 7. Retest: richiesta del cliente, date ed esecuzione
    RetestReporting, // 8. Report del retest: invio al cliente senza revisione
    Done
}

public enum ProposalParty
{
    Client,
    Provider
}

public enum ProposalStatus
{
    Pending,
    Accepted,
    Rejected,
    Superseded,
    Replaced
}

public enum ReviewStatus
{
    None,
    Pending,
    Approved,
    Changes
}

public enum AuditVisibility
{
    All,
    ProviderOnly
}

public static class Roles
{
    public const string Client = "Client";
    public const string PMO = "PMO";
    public const string Pentester = "Pentester";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";

    /// <summary>
    /// Utente dell'ente che eroga il servizio (non di un cliente):
    /// PMO, Pentester, Manager, Auditor.
    /// </summary>
    public static bool IsInternal(string role) =>
        role is PMO or Pentester or Manager or Auditor;
}

public sealed class ScopeData
{
    public string? Host { get; set; }
    public string? IpRange { get; set; }
    public string? OutOfScope { get; set; }
    public string? Window { get; set; }
    public string? Contact { get; set; }
    public string? Notes { get; set; }
    public bool Done { get; set; }
    public string? By { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public sealed class CredentialData
{
    public string? Vpn { get; set; }
    public string? Users { get; set; }
    public string? DeliveryMethod { get; set; }
    public string? Notes { get; set; }
    public bool Done { get; set; }
    public string? By { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public sealed record DistributionEntry(
    string Id,
    string Name,
    string Email,
    string Organization,
    // Persona del portale (utenza o partecipante) a cui la voce appartiene.
    // Null per gli indirizzi che ricevono solo le email.
    string? PersonId = null);

public sealed record TeamMember(
    string Id,
    string Name,
    string Email,
    string Organization,
    string? Role,
    string InvitedBy,
    DateTimeOffset InvitedDate);

public sealed class MailSettings
{
    public bool Start { get; set; } = true;
    public bool End { get; set; } = true;
    public bool Deliver { get; set; } = true;
}

public sealed class MailTemplate
{
    /// <summary>Oggetto in italiano (lingua di retrocompatibilità).</summary>
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SubjectEn { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Subject) &&
        string.IsNullOrWhiteSpace(Body) &&
        string.IsNullOrWhiteSpace(SubjectEn) &&
        string.IsNullOrWhiteSpace(BodyEn);

    /// <summary>Vero se il template ha contenuto per la lingua indicata.</summary>
    public bool HasLanguage(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(SubjectEn) ||
                !string.IsNullOrWhiteSpace(BodyEn)
            : !string.IsNullOrWhiteSpace(Subject) ||
                !string.IsNullOrWhiteSpace(Body);

    public string SubjectFor(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? SubjectEn
            : Subject;

    public string BodyFor(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
            ? BodyEn
            : Body;
}

public sealed class MailTemplateSet
{
    public MailTemplate? Start { get; set; }
    public MailTemplate? End { get; set; }
    public MailTemplate? Deliver { get; set; }
    public MailTemplate? RetestReport { get; set; }

    // Solleciti (vedi MailTemplates.Types per chiavi, etichette e destinatari).
    public MailTemplate? SolReportUpload { get; set; }
    public MailTemplate? SolReview { get; set; }
    public MailTemplate? SolCredentialsEarly { get; set; }
    public MailTemplate? SolCredentialsLate { get; set; }
    public MailTemplate? SolManual { get; set; }

    public MailTemplate? For(string type) => type switch
    {
        "start" => Start,
        "end" => End,
        "deliver" => Deliver,
        "retestreport" => RetestReport,
        "solrep" => SolReportUpload,
        "solrev" => SolReview,
        "solcred3" => SolCredentialsEarly,
        "solcred2" => SolCredentialsLate,
        "solmanual" => SolManual,
        _ => null
    };

    public void Set(string type, MailTemplate? template)
    {
        switch (type)
        {
            case "start":
                Start = template;
                break;
            case "end":
                End = template;
                break;
            case "deliver":
                Deliver = template;
                break;
            case "retestreport":
                RetestReport = template;
                break;
            case "solrep":
                SolReportUpload = template;
                break;
            case "solrev":
                SolReview = template;
                break;
            case "solcred3":
                SolCredentialsEarly = template;
                break;
            case "solcred2":
                SolCredentialsLate = template;
                break;
            case "solmanual":
                SolManual = template;
                break;
        }
    }
}

public sealed record MailLogEntry(
    string Id,
    string Type,
    DateTimeOffset Date,
    string Recipients,
    bool Auto,
    string Visibility,
    string? Subject = null,
    string? Body = null,
    string? TemplateSource = null,
    // Chi ha premuto "invia" su un sollecito: il limite giornaliero è per
    // persona, quindi serve sapere il mittente e non solo il task.
    string? SentBy = null,
    string? SentByName = null);


public static class DeliverableKinds
{
    public const string Executive = "Executive";
    public const string Technical = "Technical";
    public const string Activity = "Activity";
    public const string Custom = "Custom";

    public static readonly string[] All =
        [Executive, Technical, Activity, Custom];
}

public sealed class Engagement
{
    public required string Id { get; init; }
    // Cliente e tipologia si scelgono alla creazione ma restano correggibili
    // dal Setup (che è la scheda anagrafica dell'attività).
    public required string ClientId { get; set; }
    public required string ClientName { get; set; }
    public required string Name { get; set; }
    public required EngagementType Type { get; set; }

    /// <summary>
    /// Chiave del catalogo dei tipi di attività. I tipi standard coincidono con
    /// l'enum; quelli creati da Amministrazione hanno una chiave propria e
    /// portano <see cref="Type"/> a <see cref="EngagementType.Custom"/>.
    /// </summary>
    public string? ActivityTypeKey { get; set; }

    /// <summary>Chiave da usare ovunque al posto dell'enum.</summary>
    [JsonIgnore]
    public string TypeKey => string.IsNullOrWhiteSpace(ActivityTypeKey)
        ? Type.ToString()
        : ActivityTypeKey;

    public required string Owner { get; init; }

    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public bool HasManleva { get; set; }
    public bool OrderConfirmed { get; set; }

    /// <summary>Setup iniziale: team, liste, lingua e tipologie di deliverable.</summary>
    public bool SetupDone { get; set; } = true;
    public string Language { get; set; } = "it";
    public List<string> DeliverableTypes { get; init; } =
        [DeliverableKinds.Technical];

    /// <summary>
    /// Recheck previsto: si decide nel Setup (default dal tipo di attività) e
    /// resta modificabile fino alla richiesta di retest del cliente. Se è
    /// spento, i due passi di retest non esistono per questa attività.
    /// </summary>
    public bool RetestPlanned { get; set; }

    /// <summary>Chi e quando ha deciso se il recheck è previsto.</summary>
    public string? RetestDecidedByName { get; set; }
    public DateTimeOffset? RetestDecidedDate { get; set; }

    /// <summary>Richiesta di retest dal cliente: si può fare una sola volta.</summary>
    public DateTimeOffset? RetestRequestedDate { get; set; }
    public string? RetestRequestedByName { get; set; }

    /// <summary>Date del retest: stessa logica di proposta/accettazione del planning.</summary>
    public List<Proposal> RetestProposals { get; init; } = [];
    public Proposal? AcceptedRetestProposal { get; set; }

    /// <summary>Quando il retest è stato eseguito (report caricato).</summary>
    public DateTimeOffset? RetestCompletedDate { get; set; }

    /// <summary>Report del retest: inviato al cliente senza revisione.</summary>
    public ReportFile? RetestReport { get; set; }
    public DateTimeOffset? RetestReportSentDate { get; set; }

    /// <summary>Chiusura senza retest: l'attività si conclude comunque.</summary>
    public DateTimeOffset? RetestClosedDate { get; set; }
    public string? RetestClosedByName { get; set; }

    public bool ReportsComplete => DeliverableTypes.All(type =>
        Reports.Any(report => report.IsLatest &&
            string.Equals(report.Kind, type, StringComparison.OrdinalIgnoreCase)));

    public IReadOnlyList<string> MissingReportTypes => DeliverableTypes
        .Where(type => !Reports.Any(report => report.IsLatest &&
            string.Equals(report.Kind, type, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    public bool ScopeDone { get; set; }
    public bool CredentialsDone { get; set; }
    public string? SecretReference { get; set; }

    public string? ManlevaFile { get; set; }
    public string? DocumentoIngaggioFile { get; set; }
    public string? OrderRef { get; set; }
    public decimal? Cost { get; set; }
    /// <summary>Margine percentuale sul costo (0-100).</summary>
    public decimal? Margin { get; set; }
    public bool FinancialsApproved { get; set; }
    public string? FinancialsSetByRole { get; set; }
    public string? FinancialsSetBy { get; set; }
    public string? FinancialsApprovedBy { get; set; }
    public DateTimeOffset? FinancialsApprovedDate { get; set; }
    public bool FinancialsDone => Cost is not null && Margin is not null;
    public bool FinancialsPending => FinancialsDone && !FinancialsApproved;
    public ScopeData Scope { get; set; } = new();
    public CredentialData Credentials { get; set; } = new();
    public List<DistributionEntry> DistributionList { get; init; } = [];
    public List<DistributionEntry> DeliverablesDistributionList { get; init; } = [];
    public List<TeamMember> Team { get; init; } = [];
    public MailSettings Mail { get; set; } = new();
    public MailTemplateSet MailTemplates { get; set; } = new();
    public List<MailLogEntry> MailLog { get; init; } = [];

    public List<Proposal> Proposals { get; init; } = [];
    public List<ReportFile> Reports { get; init; } = [];
    public List<ReportDownloadLink> ReportLinks { get; init; } = [];
    public ReviewStatus ReviewStatus { get; set; }

    /// <summary>Quando i deliverable sono stati inviati in revisione.</summary>
    public DateTimeOffset? ReviewRequestedDate { get; set; }

    public List<AuditEntry> AuditLog { get; init; } = [];

    public Proposal? AcceptedProposal =>
        Proposals.FirstOrDefault(p => p.Status == ProposalStatus.Accepted);

    public void AddAudit(
        string author,
        string role,
        string text,
        AuditVisibility visibility,
        string? clientText = null)
    {
        AuditLog.Add(new AuditEntry(
            DateTimeOffset.UtcNow,
            author,
            role,
            text,
            visibility,
            clientText));
    }
}

public sealed class Proposal
{
    public required string Id { get; init; }
    public ProposalParty From { get; init; }
    public required string By { get; init; }
    public string Kind { get; init; } = "initial";
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public DateOnly Date { get; init; } =
        DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Note { get; init; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public string? Reason { get; set; }
}

public sealed record ReportFile(
    string Id,
    string Name,
    string Type,
    long Size,
    DateTimeOffset Date,
    bool Published,
    string Kind = DeliverableKinds.Technical,
    int Version = 1,
    bool IsLatest = true);

/// <summary>
/// Link di download monouso per un deliverable pubblicato. Il token è
/// l'unica credenziale: scade dopo 5 giorni e si invalida al primo uso.
/// </summary>
public sealed class ReportDownloadLink
{
    public required string Token { get; init; }
    public required string ReportId { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed record AuditEntry(
    DateTimeOffset Date,
    string Author,
    string AuthorRole,
    string Text,
    AuditVisibility Visibility,
    string? ClientText);

public sealed record TodoItem(string Action, string TargetTab);

public sealed record UserContext(
    string Name,
    string? ClientId,
    string Role,
    string? UserId = null,
    IReadOnlyCollection<string>? AssignedClientIds = null,
    IReadOnlyCollection<string>? AssignedEngagementIds = null,
    bool Scoped = false)
{
    /// <summary>Utente interno: PMO, Pentester, Manager, Auditor.</summary>
    public bool IsInternal => Roles.IsInternal(Role);
}

public sealed record CreateProposalRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Note);

public sealed record DecideProposalRequest(
    bool Accept,
    string? Reason);

public sealed record AddReportRequest(
    string Name,
    string Type,
    long Size,
    string? Kind = null);

public sealed record SetupRequest(
    string Language,
    List<string>? DeliverableTypes,
    bool? RetestPlanned = null,
    // Dati chiesti alla creazione, correggibili dal Setup.
    string? Name = null,
    string? ClientId = null,
    string? Type = null);

public sealed record ReviewDecisionRequest(
    bool Approve,
    string? Comment);

public sealed record OrderRequest(string? Ref);

public sealed record ScopeRequest(
    string? Host,
    string? IpRange,
    string? OutOfScope,
    string? Window,
    string? Contact,
    string? Notes);

public sealed record CredentialsRequest(
    string? Vpn,
    string? Users,
    string? DeliveryMethod,
    string? Notes);

public sealed record DistributionEntryRequest(
    string Name,
    string Email,
    string Organization);

/// <summary>
/// Appartenenza di una persona alle due liste base del cliente. Con
/// <see cref="PersonId"/> la voce segue l'utenza o il partecipante; senza,
/// vale l'indirizzo passato a mano (destinatario che non entra nel portale).
/// </summary>
public sealed record DistributionMembershipRequest(
    string Kind,
    bool Included,
    string? PersonId,
    string? Name,
    string? Email,
    string? Organization);

public sealed record TeamMemberRequest(
    string? Scope,
    string Name,
    string Email,
    string Organization,
    string? Role);

public sealed record AdminTemplateRequest(
    string? Subject,
    string? Body,
    bool Reset = false,
    string? SubjectEn = null,
    string? BodyEn = null);

public sealed class ClientAccount
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Website { get; set; }
    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public List<ClientUser> Users { get; init; } = [];
    public List<DistributionEntry> DistributionList { get; init; } = [];
    public List<DistributionEntry> DeliverablesDistributionList { get; init; } = [];
    public List<TeamMember> Team { get; init; } = [];
    public MailTemplateSet MailTemplates { get; set; } = new();
}

public sealed class UserAccount
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public List<string> ClientIds { get; init; } = [];
    public List<string> EngagementIds { get; init; } = [];
}

public sealed record CreateUserRequest(
    string Name,
    string Email,
    string Role);

public sealed record UpdateUserRequest(
    string Name,
    string Email,
    string Role,
    bool? Active);

public sealed record UserAssignmentRequest(
    string Type,
    string TargetId);

public sealed record ClientUser(string Id, string Name, string Email);

public sealed record CreateClientRequest(string Name, string? Website);

public sealed record RenameClientRequest(string Name, string? Website);

public sealed record FinancialsRequest(decimal? Cost, decimal? Margin);

public sealed record AddClientUserRequest(string Name, string Email);

/// <summary>
/// Modifica di una regola di sollecito. Ogni campo è facoltativo: il client
/// manda solo ciò che cambia, il resto resta com'è.
/// </summary>
public sealed record SolicitationRuleRequest(
    bool? Enabled = null,
    int? ThresholdDays = null,
    int? MinDays = null,
    bool? CountBusinessDays = null);

/// <summary>Modifica di un tipo di attività dal catalogo.</summary>
public sealed record ActivityTypeRequest(
    string? Label,
    string? Short,
    string? Description,
    bool RetestPlanned,
    List<string>? DeliverableTypes,
    bool Reset = false);

/// <summary>Date proposte per il retest (PMO) e accettazione (Manager).</summary>
public sealed record RetestProposalRequest(
    string? StartDate,
    string? EndDate);

public sealed record RetestDecisionRequest(bool Approve);

/// <summary>Report del retest caricato dal Pentester.</summary>
public sealed record RetestReportRequest(
    string Description);

public sealed record CreateEngagementRequest(
    string Name,
    string? Type,
    string? ClientId,
    string? Id);

public sealed record DocumentRequest(string? FileName);
