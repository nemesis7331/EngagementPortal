using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IEngagementRepository
{
    IReadOnlyList<Engagement> List(string tenantId, bool crossTenant);
    Engagement? Get(string id, string tenantId, bool crossTenant);
    void Save(Engagement engagement);
}

public sealed class InMemoryEngagementRepository:
    IEngagementRepository
{
    private readonly object gate = new();
    private readonly List<Engagement> engagements = Seed();

    public IReadOnlyList<Engagement> List(
        string tenantId,
        bool crossTenant)
    {
        lock (gate)
        {
            return engagements
                .Where(e => crossTenant || e.TenantId == tenantId)
                .ToList();
        }
    }

    public Engagement? Get(
        string id,
        string tenantId,
        bool crossTenant)
    {
        lock (gate)
        {
            return engagements.FirstOrDefault(e =>
                e.Id == id &&
                (crossTenant || e.TenantId == tenantId));
        }
    }

    public void Save(Engagement engagement)
    {
        lock (gate)
        {
            var index = engagements.FindIndex(e => e.Id == engagement.Id);

            if (index >= 0)
                engagements[index] = engagement;
        }
    }

    private static List<Engagement> Seed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var planning = new Engagement
        {
            Id = "ENG-2026-001",
            TenantId = "tenant-contoso",
            ClientName = "Contoso",
            Name = "External Penetration Test",
            Type = EngagementType.PenetrationTest,
            Owner = "Bip Cyberdefence",
            HasStaf = true,
            OrderConfirmed = true
        };

        planning.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Bip,
            By = "Bip Planner",
            StartDate = today.AddDays(7),
            EndDate = today.AddDays(12)
        });

        planning.AddAudit(
            "Bip Planner",
            Roles.Cyberdefence,
            "Planning proposal created",
            AuditVisibility.All);

        var documents = new Engagement
        {
            Id = "ENG-2026-002",
            TenantId = "tenant-fabrikam",
            ClientName = "Fabrikam",
            Name = "Cloud Security Review",
            Type = EngagementType.CloudSecurity,
            Owner = "Bip Cyberdefence",
            HasStaf = false,
            OrderConfirmed = false
        };

        var reporting = new Engagement
        {
            Id = "ENG-2026-003",
            TenantId = "tenant-contoso",
            ClientName = "Contoso",
            Name = "Web Application Retest",
            Type = EngagementType.PenetrationTest,
            Owner = "Bip Cyberdefence",
            HasStaf = true,
            OrderConfirmed = true,
            ScopeDone = true,
            CredentialsDone = true,
            CredentialDeliveryMethod = "Azure Key Vault",
            SecretReference = "kv://demo/eng-003",
            ReviewStatus = ReviewStatus.Pending
        };

        reporting.Proposals.Add(new Proposal
        {
            Id = "P-003",
            From = ProposalParty.Client,
            By = "Client User",
            StartDate = today.AddDays(-10),
            EndDate = today.AddDays(-5),
            Status = ProposalStatus.Accepted
        });

        reporting.Reports.Add(new ReportFile(
            "R-003",
            "Draft-report.pdf",
            "PDF",
            1_200_000,
            DateTimeOffset.UtcNow.AddDays(-1),
            false));

        reporting.AddAudit(
            "Bip Cyberdefence",
            Roles.Cyberdefence,
            "Deliverable submitted for review",
            AuditVisibility.BipOnly,
            "Deliverable is under review");

        return [planning, documents, reporting];
    }
}
