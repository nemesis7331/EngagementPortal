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
    Docs,
    Planning,
    Prep,
    Scheduled,
    Running,
    Reporting,
    Done
}

public enum ProposalParty
{
    Client,
    Bip
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
    BipOnly
}

public static class Roles
{
    public const string Client = "Client";
    public const string Cybersec = "Cybersec";
    public const string Cyberdefence = "Cyberdefence";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";

    public static bool IsBip(string role) =>
        role is Cybersec or Cyberdefence or Manager or Auditor;
}

public sealed class Engagement
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string ClientName { get; init; }
    public required string Name { get; set; }
    public required EngagementType Type { get; init; }
    public required string Owner { get; init; }

    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public bool HasStaf { get; set; }
    public bool OrderConfirmed { get; set; }

    public bool ScopeDone { get; set; }
    public bool CredentialsDone { get; set; }
    public string? CredentialDeliveryMethod { get; set; }
    public string? SecretReference { get; set; }

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
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
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
    string TenantId,
    string Role)
{
    public bool IsBip => Roles.IsBip(Role);
}

public sealed record CreateProposalRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Note);

public sealed record DecideProposalRequest(
    bool Accept,
    string? Reason);

public sealed record PreparationRequest(
    bool ScopeDone,
    bool CredentialsDone,
    string? DeliveryMethod,
    string? SecretReference);

public sealed record AddReportRequest(
    string Name,
    string Type,
    long Size);

public sealed record ReviewDecisionRequest(
    bool Approve,
    string? Comment);
