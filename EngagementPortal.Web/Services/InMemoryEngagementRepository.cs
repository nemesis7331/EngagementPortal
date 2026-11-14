using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IEngagementRepository
{
    IReadOnlyList<Engagement> List(string tenantId, bool crossTenant);
    Engagement? Get(string id, string tenantId, bool crossTenant);
    void Save(Engagement engagement);
}

public sealed class InMemoryEngagementRepository :
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
            else
                engagements.Add(engagement);
        }
    }

    private static List<Engagement> Seed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var planning = NewEngagement(
            "ENG-2026-001",
            "tenant-contoso",
            "Contoso",
            "External Penetration Test",
            EngagementType.PenetrationTest,
            6);

        planning.HasManleva = true;
        planning.ManlevaFile = "Manleva_firmata.pdf";
        planning.OrderConfirmed = true;
        planning.OrderRef = "PO-2026-0142";
        planning.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        AddDefaultDistribution(planning, "contoso.example");
        AddMailLog(planning, "start", 4);

        planning.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Provider Planner",
            StartDate = today.AddDays(7),
            EndDate = today.AddDays(12),
            Note = "Finestra proposta dal team PMO"
        });

        planning.AddAudit(
            "Luca Ferri",
            Roles.PMO,
            "Planning proposal created",
            AuditVisibility.All);

        var documents = NewEngagement(
            "ENG-2026-002",
            "tenant-fabrikam",
            "Fabrikam",
            "Cloud Security Review",
            EngagementType.CloudSecurity,
            9);

        documents.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        AddDefaultDistribution(documents, "fabrikam.example");

        documents.AddAudit(
            "Luca Ferri",
            Roles.PMO,
            "Engagement created",
            AuditVisibility.All);

        var reporting = NewEngagement(
            "ENG-2026-003",
            "tenant-contoso",
            "Contoso",
            "Web Application Retest",
            EngagementType.PenetrationTest,
            25);

        reporting.HasManleva = true;
        reporting.ManlevaFile = "Manleva_firmata.pdf";
        reporting.OrderConfirmed = true;
        reporting.OrderRef = "PO-2026-0107";
        reporting.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        reporting.ScopeDone = true;
        reporting.CredentialsDone = true;
        reporting.CredentialDeliveryMethod = "Azure Key Vault";
        reporting.SecretReference = "kv://demo/eng-003";
        reporting.Scope = new ScopeData
        {
            Host = "app.contoso.example",
            IpRange = "10.20.30.0/24",
            OutOfScope = "Ambienti di produzione",
            Window = "Lun-Ven 20:00-06:00",
            Contact = "security@contoso.example",
            Notes = "Retest dopo remediation",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-12)
        };
        reporting.Credentials = new CredentialData
        {
            Vpn = "VPN-CONTOSO-01",
            Users = "2 utenze di test",
            DeliveryMethod = "Azure Key Vault",
            Notes = "Nessuna password condivisa",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-12)
        };
        AddDefaultDistribution(reporting, "contoso.example");
        AddMailLog(reporting, "start", 10);
        AddMailLog(reporting, "end", 4);

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

        reporting.ReviewStatus = ReviewStatus.Pending;

        reporting.AddAudit(
            "Provider Pentester",
            Roles.Pentester,
            "Deliverable submitted for review",
            AuditVisibility.ProviderOnly,
            "Deliverable is under review");

        var running = NewEngagement(
            "ENG-2026-004",
            "tenant-novabank",
            "Nova Bank",
            "Internal Red Team",
            EngagementType.RedTeam,
            12);

        running.HasManleva = true;
        running.ManlevaFile = "Manleva_firmata.pdf";
        running.OrderConfirmed = true;
        running.OrderRef = "PO-2026-0188";
        running.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        running.ScopeDone = true;
        running.CredentialsDone = true;
        running.CredentialDeliveryMethod = "Portale sicuro";
        running.Scope = new ScopeData
        {
            Host = "redteam.novabank.example",
            IpRange = "10.40.0.0/16",
            OutOfScope = "Sistemi di pagamento",
            Window = "Sempre attivo",
            Contact = "soc@novabank.example",
            Notes = "Esercitazione con regole concordate",
            Done = true,
            By = "Provider Pentester",
            Date = DateTimeOffset.UtcNow.AddDays(-7)
        };
        running.Credentials = new CredentialData
        {
            Vpn = "VPN-NOVA-02",
            Users = "4 utenze dedicate",
            DeliveryMethod = "Portale sicuro",
            Notes = "Credenziali consegnate tramite canale cifrato",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-7)
        };
        AddDefaultDistribution(running, "novabank.example");
        AddMailLog(running, "start", 3);

        running.Proposals.Add(new Proposal
        {
            Id = "P-004",
            From = ProposalParty.Provider,
            By = "Provider Planner",
            StartDate = today.AddDays(-3),
            EndDate = today.AddDays(4),
            Status = ProposalStatus.Accepted
        });

        running.Reports.Add(new ReportFile(
            "R-004",
            "Red-team-draft.pdf",
            "PDF",
            840_000,
            DateTimeOffset.UtcNow,
            false));

        running.AddAudit(
            "Luca Ferri",
            Roles.PMO,
            "Engagement started",
            AuditVisibility.All);

        var scheduled = NewEngagement(
            "ENG-2026-005",
            "tenant-orion",
            "Orion Energia",
            "Cloud Security Assessment",
            EngagementType.CloudSecurity,
            4);

        scheduled.HasManleva = true;
        scheduled.ManlevaFile = "Manleva_firmata.pdf";
        scheduled.OrderConfirmed = true;
        scheduled.OrderRef = "PO-2026-0201";
        scheduled.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        scheduled.ScopeDone = true;
        scheduled.CredentialsDone = true;
        scheduled.CredentialDeliveryMethod = "Azure Key Vault";
        scheduled.Scope = new ScopeData
        {
            Host = "cloud.orion.example",
            IpRange = "Sottoscrizioni Azure",
            OutOfScope = "Tenant di produzione",
            Window = "Lun-Ven 09:00-18:00",
            Contact = "cloud-security@orion.example",
            Notes = "Assessment su ambiente di pre-produzione",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-2)
        };
        scheduled.Credentials = new CredentialData
        {
            Vpn = "Non richiesta",
            Users = "Service principal di sola lettura",
            DeliveryMethod = "Azure Key Vault",
            Notes = "Nessuna password condivisa",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-2)
        };
        AddDefaultDistribution(scheduled, "orion.example");

        scheduled.Proposals.Add(new Proposal
        {
            Id = "P-005",
            From = ProposalParty.Provider,
            By = "Provider Planner",
            StartDate = today.AddDays(7),
            EndDate = today.AddDays(17),
            Status = ProposalStatus.Accepted
        });

        scheduled.AddAudit(
            "Luca Ferri",
            Roles.PMO,
            "Planning proposal accepted",
            AuditVisibility.All);

        var done = NewEngagement(
            "ENG-2026-006",
            "tenant-novabank",
            "Nova Bank",
            "Web Application Penetration Test",
            EngagementType.PenetrationTest,
            40);

        done.HasManleva = true;
        done.ManlevaFile = "Manleva_firmata.pdf";
        done.OrderConfirmed = true;
        done.OrderRef = "PO-2026-0091";
        done.DocumentoIngaggioFile = "Documento_di_ingaggio.pdf";
        done.ScopeDone = true;
        done.CredentialsDone = true;
        done.CredentialDeliveryMethod = "Azure Key Vault";
        done.Scope = new ScopeData
        {
            Host = "portal.novabank.example",
            IpRange = "10.50.10.0/24",
            OutOfScope = "App mobile",
            Window = "Lun-Ven 20:00-06:00",
            Contact = "appsec@novabank.example",
            Notes = "Test completato",
            Done = true,
            By = "Provider Pentester",
            Date = DateTimeOffset.UtcNow.AddDays(-35)
        };
        done.Credentials = new CredentialData
        {
            Vpn = "VPN-NOVA-01",
            Users = "3 utenze di test",
            DeliveryMethod = "Azure Key Vault",
            Notes = "Credenziali revocate a fine test",
            Done = true,
            By = "Client User",
            Date = DateTimeOffset.UtcNow.AddDays(-35)
        };
        AddDefaultDistribution(done, "novabank.example");
        AddMailLog(done, "start", 30);
        AddMailLog(done, "end", 21);
        AddMailLog(done, "deliver", 20, false);

        done.Proposals.Add(new Proposal
        {
            Id = "P-006",
            From = ProposalParty.Provider,
            By = "Provider Planner",
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(-20),
            Status = ProposalStatus.Accepted
        });

        done.Reports.Add(new ReportFile(
            "R-006",
            "Final-report.pdf",
            "PDF",
            2_400_000,
            DateTimeOffset.UtcNow.AddDays(-21),
            true));
        done.ReviewStatus = ReviewStatus.Approved;

        done.AddAudit(
            "Provider Pentester",
            Roles.Pentester,
            "Deliverables published",
            AuditVisibility.All);

        return
        [
            planning,
            documents,
            reporting,
            running,
            scheduled,
            done
        ];
    }

    private static Engagement NewEngagement(
        string id,
        string tenantId,
        string clientName,
        string name,
        EngagementType type,
        int createdDaysAgo) => new()
        {
            Id = id,
            TenantId = tenantId,
            ClientName = clientName,
            Name = name,
            Type = type,
            Owner = "Provider Pentester",
            CreatedDate = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo)
        };

    private static void AddDefaultDistribution(
        Engagement engagement,
        string clientDomain)
    {
        engagement.DistributionList.AddRange(
        [
            new DistributionEntry(
                $"DL-{engagement.Id}-1",
                "Anna Rossi",
                $"anna.rossi@{clientDomain}",
                "Cliente"),
            new DistributionEntry(
                $"DL-{engagement.Id}-2",
                "Luca Ferri",
                "luca.ferri@provider.example",
                "Provider PMO"),
            new DistributionEntry(
                $"DL-{engagement.Id}-3",
                "Sara Conti",
                "sara.conti@provider.example",
                "Provider Pentester")
        ]);
    }

    private static void AddMailLog(
        Engagement engagement,
        string type,
        int daysAgo,
        bool auto = true)
    {
        engagement.MailLog.Add(new MailLogEntry(
            $"M-{engagement.Id}-{type}",
            type,
            DateTimeOffset.UtcNow.AddDays(-daysAgo),
            string.Join(
                "; ",
                engagement.DistributionList.Select(entry => entry.Email)),
            auto,
            "All"));
    }
}
