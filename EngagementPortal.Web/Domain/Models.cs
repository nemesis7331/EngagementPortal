namespace EngagementPortal.Web.Domain;

public enum EngagementType
{
    PenetrationTest,
    VulnerabilityAssessment,
    RedTeam,
    CodeReview,
    CloudSecurity
}

public enum EngagementPhase
{
    Docs,        // 1. Ordine e manleva
    Scope,       // 2. Scope
    Planning,    // 3. Pianificazione
    Credentials, // 4. Credenziali
    Scheduled,   // 5. Esecuzione (prima dell'avvio)
    Running,     // 5. Esecuzione (in corso)
    Reporting,   // 6. Report
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
    string Organization);

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
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Subject) &&
        string.IsNullOrWhiteSpace(Body);
}

public sealed class MailTemplateSet
{
    public MailTemplate? Start { get; set; }
    public MailTemplate? End { get; set; }
    public MailTemplate? Deliver { get; set; }

    public MailTemplate? For(string type) => type switch
    {
        "start" => Start,
        "end" => End,
        "deliver" => Deliver,
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
    string? TemplateSource = null);


public sealed class Engagement
{
    public required string Id { get; init; }
    public required string ClientId { get; init; }
    public required string ClientName { get; set; }
    public required string Name { get; set; }
    public required EngagementType Type { get; init; }
    public required string Owner { get; init; }

    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public bool HasManleva { get; set; }
    public bool OrderConfirmed { get; set; }

    public bool ScopeDone { get; set; }
    public bool CredentialsDone { get; set; }
    public string? CredentialDeliveryMethod { get; set; }
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
    public ReviewStatus ReviewStatus { get; set; }
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
    bool Published);

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
    string Role)
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
    long Size);

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

public sealed record TeamMemberRequest(
    string? Scope,
    string Name,
    string Email,
    string Organization,
    string? Role);

public sealed record MailTemplateUpdateRequest(
    string Scope,
    string? Subject,
    string? Body,
    bool Reset = false);

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

public sealed record ClientUser(string Id, string Name, string Email);

public sealed record CreateClientRequest(string Name, string? Website);

public sealed record RenameClientRequest(string Name, string? Website);

public sealed record FinancialsRequest(decimal? Cost, decimal? Margin);

public sealed record AddClientUserRequest(string Name, string Email);

public sealed record SolicitationRuleRequest(
    bool Enabled,
    int ThresholdDays);

public sealed record CreateEngagementRequest(
    string Name,
    EngagementType Type,
    string? ClientId,
    string? Id);

public sealed record DocumentRequest(string? FileName);
