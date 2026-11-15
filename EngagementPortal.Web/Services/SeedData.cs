using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Dati demo significativi: coprono tutte le fasi del workflow, clienti
/// diversi, target e credenziali inventati, costi/margine approvati e un
/// caso con approvazione economica in attesa.
/// </summary>
public static class SeedData
{
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateTimeOffset DaysAgo(int days) =>
        DateTimeOffset.UtcNow.AddDays(-days);

    public static List<Engagement> Engagements()
    {
        var engagements = new List<Engagement>
        {
            // 1. Scope: documenti completi, manca lo scope; proposta in attesa.
            ScopeCase(),

            // 2. Primo passo da completare (documenti e costi mancanti).
            DocumentsCase(),

            // 3. Report: revisione manager in attesa.
            ReportingPendingCase(),

            // 4. Esecuzione in corso.
            RunningCase(),

            // 5. Pianificato, in attesa della data di avvio.
            ScheduledCase(),

            // 6. Concluso con deliverable pubblicato.
            DoneCase(),

            // 7. Scope a rischio: avvio tra 9 giorni, scope/credenziali mancanti.
            AtRiskCase(),

            // 8. Credenziali: scope e date ci sono, mancano gli accessi.
            CredentialsCase(),

            // 9. Pianificazione: scope pronto, nessuna proposta di date.
            PlanningCase(),

            // 10. Pianificato: assessment rete OT.
            OtScheduledCase(),

            // 11. Report: modifiche richieste dal manager.
            ReportingChangesCase(),

            // 12. Costi inseriti dal PMO, in attesa di approvazione Manager.
            PendingFinancialsCase(),

            // 13. Esecuzione: nuovo cliente Anthropic.
            AnthropicRunningCase(),

            // 14. Pianificato: nuovo cliente Nvidia.
            NvidiaScheduledCase(),

            // 15. Concluso per Anthropic: il profilo Cliente vede un report
            //     pubblicato e scaricabile.
            DoneAnthropicCase()
        };

        return engagements;
    }

    private static Engagement ScopeCase()
    {
        var engagement = New(
            "ENG-2026-001", "anthropic", "Anthropic",
            "External Penetration Test", EngagementType.PenetrationTest, 6);

        Docs(engagement, "PO-2026-0142");
        Financials(engagement, 12000m, 46m);
        Distribution(engagement);
        Proposal(engagement, "P-001", Today.AddDays(7), Today.AddDays(12),
            ProposalStatus.Pending, note: "Finestra proposta dal team PMO");
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione creata");
        return engagement;
    }

    private static Engagement DocumentsCase()
    {
        var engagement = New(
            "ENG-2026-002", "nvidia", "Nvidia",
            "Cloud Security Review", EngagementType.CloudSecurity, 9);

        engagement.DocumentoIngaggioFile =
            $"samples/documents/{engagement.Id}-ingaggio.txt";
        Distribution(engagement);
        Audit(engagement, "Luca Ferri", Roles.PMO, "Engagement creato");
        return engagement;
    }

    private static Engagement ReportingPendingCase()
    {
        var engagement = New(
            "ENG-2026-003", "anthropic", "Anthropic",
            "Web Application Retest", EngagementType.PenetrationTest, 25);

        Docs(engagement, "PO-2026-0107");
        Financials(engagement, 9000m, 46m);
        Scope(engagement,
            "app.anthropic.example", "10.20.30.0/24",
            "Ambienti di produzione", "Lun-Ven 20:00-06:00",
            "security@anthropic.example", "Retest dopo remediation", 12);
        Credentials(engagement,
            "VPN-ANTHROPIC-01", "2 utenze di test",
            "Azure Key Vault", "Nessuna password condivisa", 12);
        Distribution(engagement);
        Mail(engagement, "start", 10);
        Mail(engagement, "end", 4);
        Proposal(engagement, "P-003", Today.AddDays(-10), Today.AddDays(-5),
            ProposalStatus.Accepted, ProposalParty.Client, "Marta Rinaldi");
        engagement.DeliverableTypes.Clear();
        engagement.DeliverableTypes.AddRange(
        [
            DeliverableKinds.Executive,
            DeliverableKinds.Technical,
            DeliverableKinds.Activity
        ]);
        Report(engagement, "R-003",
            "Report_ENG-2026-003_executive.txt", false, 1,
            DeliverableKinds.Executive);
        Report(engagement, "R-003-T",
            "Report_ENG-2026-003_bozza.txt", false, 1,
            DeliverableKinds.Technical);
        Report(engagement, "R-003-A",
            "Report_ENG-2026-003_attivita.txt", false, 1,
            DeliverableKinds.Activity);
        TeamAdd(engagement, "TM-ENG-2026-003-1", "Marco Verdi",
            "marco.verdi@studioverdi.example", "Partner",
            "Consulente legale");
        engagement.ReviewStatus = ReviewStatus.Pending;
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Deliverable inviato in revisione");
        return engagement;
    }

    private static Engagement RunningCase()
    {
        var engagement = New(
            "ENG-2026-004", "nvidia", "Nvidia",
            "Internal Red Team", EngagementType.RedTeam, 12);

        Docs(engagement, "PO-2026-0188");
        Financials(engagement, 18000m, 46m);
        Scope(engagement,
            "redteam.nvidia.example", "10.40.0.0/16",
            "Sistemi di pagamento", "Sempre attivo",
            "soc@nvidia.example", "Esercitazione con regole concordate", 7);
        Credentials(engagement,
            "VPN-NOVA-02", "4 utenze dedicate",
            "Portale sicuro", "Credenziali consegnate su canale cifrato", 7);
        Distribution(engagement);
        Mail(engagement, "start", 3);
        Proposal(engagement, "P-004", Today.AddDays(-3), Today.AddDays(4),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO, "Engagement avviato");
        return engagement;
    }

    private static Engagement ScheduledCase()
    {
        var engagement = New(
            "ENG-2026-005", "anthropic", "Anthropic",
            "Cloud Security Assessment", EngagementType.CloudSecurity, 4);

        Docs(engagement, "PO-2026-0201");
        Financials(engagement, 15000m, 46m);
        Scope(engagement,
            "cloud.anthropic.example", "Sottoscrizioni Azure",
            "Ambiente di produzione", "Lun-Ven 09:00-18:00",
            "cloud-security@anthropic.example",
            "Assessment su ambiente di pre-produzione", 2);
        Credentials(engagement,
            "Non richiesta", "Service principal di sola lettura",
            "Azure Key Vault", "Nessuna password condivisa", 2);
        Distribution(engagement);
        Proposal(engagement, "P-005", Today.AddDays(7), Today.AddDays(17),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione accettata");
        return engagement;
    }

    private static Engagement DoneCase()
    {
        var engagement = New(
            "ENG-2026-006", "nvidia", "Nvidia",
            "Web Application Penetration Test",
            EngagementType.PenetrationTest, 40);

        Docs(engagement, "PO-2026-0091");
        Financials(engagement, 20000m, 46m);
        Scope(engagement,
            "portal.nvidia.example", "10.50.10.0/24",
            "App mobile", "Lun-Ven 20:00-06:00",
            "appsec@nvidia.example", "Test completato", 35);
        Credentials(engagement,
            "VPN-NOVA-01", "3 utenze di test",
            "Azure Key Vault", "Credenziali revocate a fine test", 35);
        Distribution(engagement);
        Mail(engagement, "start", 30);
        Mail(engagement, "end", 21);
        Mail(engagement, "deliver", 20);
        Proposal(engagement, "P-006", Today.AddDays(-30), Today.AddDays(-20),
            ProposalStatus.Accepted);
        Report(engagement, "R-006",
            "Report_ENG-2026-006_finale.txt", true, 21);
        engagement.ReviewStatus = ReviewStatus.Approved;
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Deliverable pubblicati");

        // Retest concluso: richiesto dal cliente, date approvate, report
        // inviato senza revisione. Caso demo del percorso completo.
        engagement.RetestRequestedDate = DaysAgo(18);
        engagement.RetestRequestedByName = "Giulia Sala";
        engagement.RetestProposals.Add(new Proposal
        {
            Id = "RE-01",
            By = "Luca Ferri",
            Kind = "retest",
            StartDate = Today.AddDays(-15),
            EndDate = Today.AddDays(-13),
            Date = Today.AddDays(-16),
            Status = ProposalStatus.Accepted,
            Note = "Retest dopo i fix"
        });
        engagement.AcceptedRetestProposal = engagement.RetestProposals[0];
        engagement.RetestCompletedDate = DaysAgo(12);
        engagement.RetestReport = new ReportFile(
            "R-006-RT",
            "Report_ENG-2026-006_retest.txt",
            "PDF",
            0,
            DaysAgo(12),
            true,
            "Retest");
        engagement.RetestReportSentDate = DaysAgo(11);
        Mail(engagement, "retestreport", 11);
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Report di retest inviato al cliente (senza revisione)");
        return engagement;
    }

    private static Engagement DoneAnthropicCase()
    {
        var engagement = New(
            "ENG-2026-015", "anthropic", "Anthropic",
            "Internal Vulnerability Assessment",
            EngagementType.VulnerabilityAssessment, 45);

        Docs(engagement, "PO-2026-0088");
        Financials(engagement, 7000m, 46m);
        Scope(engagement,
            "intranet.anthropic.example", "10.20.50.0/24",
            "Sistemi di produzione", "Lun-Ven 20:00-06:00",
            "security@anthropic.example", "Assessment concluso", 40);
        Credentials(engagement,
            "VPN-ANTHROPIC-02", "2 utenze di test",
            "Azure Key Vault", "Credenziali revocate a fine test", 40);
        Distribution(engagement);
        Mail(engagement, "start", 35);
        Mail(engagement, "end", 26);
        Mail(engagement, "deliver", 25);
        Proposal(engagement, "P-015", Today.AddDays(-35), Today.AddDays(-25),
            ProposalStatus.Accepted);
        engagement.DeliverableTypes.Clear();
        engagement.DeliverableTypes.AddRange(
        [
            DeliverableKinds.Executive,
            DeliverableKinds.Technical
        ]);
        Report(engagement, "R-015",
            "Report_ENG-2026-015_v1.txt", true, 28,
            DeliverableKinds.Technical, 1);
        Report(engagement, "R-015-V2",
            "Report_ENG-2026-015_finale.txt", true, 25,
            DeliverableKinds.Technical, 2);
        Report(engagement, "R-015-E",
            "Report_ENG-2026-015_executive.txt", true, 25,
            DeliverableKinds.Executive);
        engagement.ReviewStatus = ReviewStatus.Approved;
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Deliverable pubblicati");
        return engagement;
    }

    private static Engagement AtRiskCase()
    {
        var engagement = New(
            "ENG-2026-007", "anthropic", "Anthropic",
            "Cloud Configuration Review", EngagementType.CloudSecurity, 5);

        Docs(engagement, "PO-2026-0197");
        Financials(engagement, 11000m, 46m);
        Distribution(engagement);
        Proposal(engagement, "P-007", Today.AddDays(9), Today.AddDays(19),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione accettata");
        return engagement;
    }

    private static Engagement CredentialsCase()
    {
        var engagement = New(
            "ENG-2026-008", "nvidia", "Nvidia",
            "Payment API Security Review", EngagementType.CodeReview, 8);

        Docs(engagement, "PO-2026-0164");
        Financials(engagement, 14000m, 46m);
        Scope(engagement,
            "pay-api.nvidia.example", "10.20.40.0/24",
            "Produzione", "Lun-Ven 21:00-06:00",
            "appsec@nvidia.example",
            "Revisione dei servizi di pagamento", 6);
        Distribution(engagement);
        Proposal(engagement, "P-008", Today.AddDays(14), Today.AddDays(24),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Scope completato, credenziali in attesa");
        return engagement;
    }

    private static Engagement PlanningCase()
    {
        var engagement = New(
            "ENG-2026-009", "anthropic", "Anthropic",
            "Mobile App Penetration Test",
            EngagementType.PenetrationTest, 3);

        Docs(engagement, "PO-2026-0210");
        Financials(engagement, 16000m, 46m);
        Scope(engagement,
            "mobile.anthropic.example", "10.30.0.0/16",
            "Backend core", "Sempre attivo",
            "security@anthropic.example",
            "Test su build di pre-produzione", 2);
        Distribution(engagement);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Scope completato, pianificazione da avviare");
        return engagement;
    }

    private static Engagement OtScheduledCase()
    {
        var engagement = New(
            "ENG-2026-010", "nvidia", "Nvidia",
            "OT Network Segmentation Review",
            EngagementType.VulnerabilityAssessment, 15);

        Docs(engagement, "PO-2026-0177");
        Financials(engagement, 24000m, 46m);
        Scope(engagement,
            "scada.nvidia.example", "10.60.0.0/16",
            "Sistemi di controllo", "Notturno 22:00-06:00",
            "ot-security@nvidia.example",
            "Verifica della segmentazione rete OT", 12);
        Credentials(engagement,
            "VPN-NVIDIA-OT", "4 utenze dedicate",
            "Vault aziendale",
            "Accesso limitato alla finestra notturna", 12);
        Distribution(engagement);
        Proposal(engagement, "P-010", Today.AddDays(21), Today.AddDays(30),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Pianificazione confermata");
        return engagement;
    }

    private static Engagement ReportingChangesCase()
    {
        var engagement = New(
            "ENG-2026-011", "anthropic", "Anthropic",
            "Third-Party Risk Assessment",
            EngagementType.VulnerabilityAssessment, 20);

        Docs(engagement, "PO-2026-0129");
        Financials(engagement, 13000m, 46m);
        Scope(engagement,
            "thirdparty.anthropic.example", "10.50.20.0/24",
            "Dati di produzione", "Lun-Ven 09:00-18:00",
            "vendorrisk@anthropic.example",
            "Assessment del fornitore critico", 16);
        Credentials(engagement,
            "VPN-NOVA-03", "2 utenze fornitore",
            "File cifrato (password su canale separato)",
            "Credenziali condivise con il fornitore", 16);
        Distribution(engagement);
        Mail(engagement, "start", 14);
        Mail(engagement, "end", 7);
        Proposal(engagement, "P-011", Today.AddDays(-15), Today.AddDays(-8),
            ProposalStatus.Accepted, ProposalParty.Client, "Giulia Sala");
        Report(engagement, "R-011",
            "Report_ENG-2026-011_fornitore.txt", false, 2);
        TeamAdd(engagement, "TM-ENG-2026-011-1", "Chiara Neri",
            "chiara.neri@studioverdi.example", "Partner",
            "Referente del fornitore");
        engagement.ReviewStatus = ReviewStatus.Changes;
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Deliverable inviato in revisione");
        return engagement;
    }

    private static Engagement PendingFinancialsCase()
    {
        var engagement = New(
            "ENG-2026-012", "nvidia", "Nvidia",
            "Supply Chain Security Review",
            EngagementType.CloudSecurity, 2);

        Docs(engagement, "PO-2026-0215");
        Financials(engagement, 10000m, 46m,
            approved: false,
            setByRole: Roles.PMO,
            setBy: "Luca Ferri");
        Distribution(engagement);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Costi e margine inseriti, in attesa di approvazione");
        return engagement;
    }

    private static Engagement AnthropicRunningCase()
    {
        var engagement = New(
            "ENG-2026-013", "anthropic", "Anthropic",
            "E-commerce Red Team", EngagementType.RedTeam, 10);

        Docs(engagement, "PO-2026-0194");
        Financials(engagement, 30000m, 46m);
        Scope(engagement,
            "shop.anthropic.example", "10.90.0.0/16",
            "Pagamenti", "Lun-Ven 22:00-06:00",
            "security@anthropic.example",
            "Red team sull'e-commerce", 7);
        Credentials(engagement,
            "VPN-ANTHROPIC-01", "5 utenze di test",
            "Azure Key Vault", "Credenziali temporanee", 7);
        Distribution(engagement);
        Mail(engagement, "start", 4);
        Proposal(engagement, "P-013", Today.AddDays(-2), Today.AddDays(8),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO, "Engagement avviato");
        return engagement;
    }

    private static Engagement NvidiaScheduledCase()
    {
        var engagement = New(
            "ENG-2026-014", "nvidia", "Nvidia",
            "SAP S/4HANA Security Assessment",
            EngagementType.VulnerabilityAssessment, 18);

        Docs(engagement, "PO-2026-0151");
        Financials(engagement, 28000m, 46m);
        Scope(engagement,
            "sap.nvidia.example", "10.80.0.0/16",
            "Sistemi finanziari", "Lun-Ven 20:00-06:00",
            "sap-security@nvidia.example",
            "Assessment SAP S/4HANA", 14);
        Credentials(engagement,
            "VPN-NVIDIA-01", "3 utenze di test",
            "Vault aziendale", "Accesso al mandante", 14);
        Distribution(engagement);
        Proposal(engagement, "P-014", Today.AddDays(12), Today.AddDays(22),
            ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Pianificazione confermata");
        return engagement;
    }

    private static Engagement New(
        string id,
        string clientId,
        string clientName,
        string name,
        EngagementType type,
        int createdDaysAgo,
        bool? retestPlanned = null) => new()
        {
            Id = id,
            ClientId = clientId,
            ClientName = clientName,
            Name = name,
            Type = type,
            Owner = "Sara Conti",
            CreatedDate = DaysAgo(createdDaysAgo),
            // Default del catalogo tipi: il Red Team non prevede il retest.
            RetestPlanned = retestPlanned ?? type != EngagementType.RedTeam,
            RetestDecidedByName = "Luca Ferri",
            RetestDecidedDate = DaysAgo(createdDaysAgo)
        };

    private static void Docs(Engagement engagement, string orderRef)
    {
        engagement.HasManleva = true;
        engagement.ManlevaFile =
            $"samples/documents/{engagement.Id}-manleva.txt";
        engagement.OrderConfirmed = true;
        engagement.OrderRef = orderRef;
        engagement.DocumentoIngaggioFile =
            $"samples/documents/{engagement.Id}-ingaggio.txt";
    }

    private static void Scope(
        Engagement engagement,
        string host,
        string ipRange,
        string outOfScope,
        string window,
        string contact,
        string notes,
        int daysAgo)
    {
        engagement.ScopeDone = true;
        engagement.Scope = new ScopeData
        {
            Host = host,
            IpRange = ipRange,
            OutOfScope = outOfScope,
            Window = window,
            Contact = contact,
            Notes = notes,
            Done = true,
            By = "Client User",
            Date = DaysAgo(daysAgo)
        };
    }

    private static void Credentials(
        Engagement engagement,
        string vpn,
        string users,
        string deliveryMethod,
        string notes,
        int daysAgo)
    {
        engagement.CredentialsDone = true;
        engagement.CredentialDeliveryMethod = deliveryMethod;
        engagement.SecretReference = $"kv://demo/{engagement.Id}";
        engagement.Credentials = new CredentialData
        {
            Vpn = vpn,
            Users = users,
            DeliveryMethod = deliveryMethod,
            Notes = notes,
            Done = true,
            By = "Client User",
            Date = DaysAgo(daysAgo)
        };
    }

    private static void Financials(
        Engagement engagement,
        decimal cost,
        decimal marginPercent,
        bool approved = true,
        string setByRole = Roles.Manager,
        string setBy = "Giorgio Marino")
    {
        engagement.Cost = cost;
        engagement.Margin = marginPercent;
        engagement.FinancialsApproved = approved;
        engagement.FinancialsSetByRole = setByRole;
        engagement.FinancialsSetBy = setBy;
        engagement.FinancialsApprovedBy = approved ? "Giorgio Marino" : null;
        engagement.FinancialsApprovedDate =
            approved ? DateTimeOffset.UtcNow : null;
    }

    private static void Distribution(Engagement engagement)
    {
        engagement.DistributionList.AddRange(
        [
            new DistributionEntry(
                $"DL-{engagement.Id}-1",
                "Luca Ferri",
                "luca.ferri@provider.example",
                "Provider PMO"),
            new DistributionEntry(
                $"DL-{engagement.Id}-2",
                "Sara Conti",
                "sara.conti@provider.example",
                "Provider Pentester")
        ]);

        engagement.DeliverablesDistributionList.AddRange(
        [
            new DistributionEntry(
                $"DL-{engagement.Id}-D1",
                "Luca Ferri",
                "luca.ferri@provider.example",
                "Provider PMO"),
            new DistributionEntry(
                $"DL-{engagement.Id}-D2",
                $"Referente tecnico {engagement.ClientName}",
                $"tech@{engagement.ClientId}.example",
                "Cliente")
        ]);
    }

    private static void Proposal(
        Engagement engagement,
        string id,
        DateOnly start,
        DateOnly end,
        ProposalStatus status,
        ProposalParty from = ProposalParty.Provider,
        string by = "Luca Ferri",
        string? note = null)
    {
        engagement.Proposals.Add(new Proposal
        {
            Id = id,
            From = from,
            By = by,
            StartDate = start,
            EndDate = end,
            Date = DateOnly.FromDateTime(DaysAgo(2).DateTime),
            Note = note,
            Status = status
        });
    }

    private static void Mail(Engagement engagement, string type, int daysAgo)
    {
        var isDeliver = type is "deliver" or "retestreport";
        var list = isDeliver
            ? engagement.DeliverablesDistributionList
            : engagement.DistributionList;
        var baseRecipients = isDeliver
            ? DeliverablesContacts(engagement.ClientId)
            : NotificationContacts(engagement.ClientId);

        engagement.MailLog.Add(new MailLogEntry(
            $"M-{engagement.Id}-{type}",
            type,
            DaysAgo(daysAgo),
            string.Join(
                "; ",
                baseRecipients
                    .Concat(list.Select(entry => entry.Email))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
            true,
            "All"));
    }

    // Destinatari base demo del cliente: rispecchiano il seed di
    // InMemoryClientRepository (utenti per le notifiche, referente security
    // per i deliverables).
    private static string[] NotificationContacts(string clientId) =>
        clientId switch
        {
            "anthropic" => [
                "marta.rinaldi@anthropic.example",
                "anna.rossi@anthropic.example"],
            "nvidia" => ["giulia.sala@nvidia.example"],
            _ => []
        };

    private static string[] DeliverablesContacts(string clientId) =>
        clientId switch
        {
            "anthropic" => ["security@anthropic.example"],
            "nvidia" => ["security@nvidia.example"],
            _ => []
        };

    private static void Report(
        Engagement engagement,
        string id,
        string name,
        bool published,
        int daysAgo,
        string kind = DeliverableKinds.Technical,
        int version = 0)
    {
        if (version <= 0)
        {
            version = engagement.Reports
                .Where(report => string.Equals(
                    report.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Select(report => report.Version)
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        for (var index = 0; index < engagement.Reports.Count; index++)
        {
            var report = engagement.Reports[index];
            if (string.Equals(report.Kind, kind,
                    StringComparison.OrdinalIgnoreCase) && report.IsLatest)
            {
                engagement.Reports[index] =
                    report with { IsLatest = false };
            }
        }

        engagement.Reports.Add(new ReportFile(
            id,
            name,
            "TXT",
            2048,
            DaysAgo(daysAgo),
            published,
            kind,
            version,
            true));
    }

    private static void TeamAdd(
        Engagement engagement,
        string id,
        string name,
        string email,
        string organization,
        string role)
    {
        engagement.Team.Add(new TeamMember(
            id,
            name,
            email,
            organization,
            role,
            "Sistema",
            DaysAgo(2)));
    }

    private static void Audit(
        Engagement engagement,
        string author,
        string role,
        string text)
    {
        engagement.AddAudit(author, role, text, AuditVisibility.All);
    }
}
