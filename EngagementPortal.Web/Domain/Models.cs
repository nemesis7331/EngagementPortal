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
    public const string Cybersec = "Cybersec";
    public const string Cyberdefence = "Cyberdefence";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";

    public static bool IsProvider(string role) =>
        role is Cybersec or Cyberdefence or Manager or Auditor;
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

public sealed class MailSettings
{
    public bool Start { get; set; } = true;
    public bool End { get; set; } = true;
    public bool Deliver { get; set; } = true;
}

public sealed record MailLogEntry(
    string Id,
    string Type,
    DateTimeOffset Date,
    string Recipients,
    bool Auto,
    string Visibility);


public sealed class Engagement
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string ClientName { get; init; }
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
    public string? BusinessCaseFile { get; set; }
    public ScopeData Scope { get; set; } = new();
    public CredentialData Credentials { get; set; } = new();
    public List<DistributionEntry> DistributionList { get; init; } = [];
    public MailSettings Mail { get; set; } = new();
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
    string TenantId,
    string Role)
{
    public bool IsProvider => Roles.IsProvider(Role);
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

public sealed record SolicitationRuleRequest(
    bool Enabled,
    int ThresholdDays);

public sealed record CreateEngagementRequest(
    string Name,
    string ClientName,
    EngagementType Type,
    string? TenantId,
    string? Id);

public sealed record DocumentRequest(string? FileName);
