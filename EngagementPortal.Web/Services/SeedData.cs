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

    private static DateTimeOffset At(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// Lunedì della settimana corrente (0) o di una settimana prima/ dopo:
    /// le attività demo partono sempre a inizio settimana.
    /// </summary>
    private static DateOnly Monday(int weeksFromThisWeek = 0)
    {
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        return monday.AddDays(7 * weeksFromThisWeek);
    }

    /// <summary>
    /// Venerdì di chiusura della finestra: una settimana standard, tre o
    /// quattro per le attività lunghe (red team).
    /// </summary>
    private static DateOnly Friday(DateOnly monday, int weeks = 1) =>
        monday.AddDays(7 * weeks - 3);

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
            "PT perimetro esterno", EngagementType.PenetrationTest, 6);

        var start = Monday(2);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0142");
        Financials(engagement, 12000m, 46m);
        Distribution(engagement);
        Proposal(engagement, "P-001", start, end,
            ProposalStatus.Pending, note: "Finestra proposta dal team PMO");
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione creata");
        return engagement;
    }

    private static Engagement DocumentsCase()
    {
        var engagement = New(
            "ENG-2026-002", "nvidia", "Nvidia",
            "VA ambiente Azure", EngagementType.CloudSecurity, 9);

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
            "WAPT portale clienti", EngagementType.PenetrationTest, 25);

        var start = Monday(-1);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0107");
        Financials(engagement, 9000m, 46m);
        Scope(engagement,
            "app.anthropic.example", "10.20.30.0/24",
            "Ambienti di produzione", "Lun-Ven 20:00-06:00",
            "security@anthropic.example", "Retest dopo remediation",
            start.AddDays(-3));
        Credentials(engagement,
            "VPN-ANTHROPIC-01", "2 utenze di test",
            "Azure Key Vault", "Nessuna password condivisa",
            start.AddDays(-3));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Mail(engagement, "end", start.AddDays(3));
        Proposal(engagement, "P-003", start, end,
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
        SolicitationMail(engagement, "review", Today.AddDays(-2),
            "giorgio.marino@provider.example",
            "Sollecito: revisione dei deliverable ENG-2026-003");
        return engagement;
    }

    private static Engagement RunningCase()
    {
        var engagement = New(
            "ENG-2026-004", "nvidia", "Nvidia",
            "RT rete interna", EngagementType.RedTeam, 25);

        // Red team: quattro settimane, dal lunedì al venerdì.
        var start = Monday(-2);
        var end = Friday(start, 4);

        Docs(engagement, "PO-2026-0188");
        Financials(engagement, 18000m, 46m);
        Scope(engagement,
            "redteam.nvidia.example", "10.40.0.0/16",
            "Sistemi di pagamento", "Sempre attivo",
            "soc@nvidia.example", "Esercitazione con regole concordate",
            start.AddDays(-4));
        Credentials(engagement,
            "VPN-NOVA-02", "4 utenze dedicate",
            "Portale sicuro", "Credenziali consegnate su canale cifrato",
            start.AddDays(-4));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Proposal(engagement, "P-004", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO, "Engagement avviato");
        return engagement;
    }

    private static Engagement ScheduledCase()
    {
        var engagement = New(
            "ENG-2026-005", "anthropic", "Anthropic",
            "VA cloud pre-produzione", EngagementType.CloudSecurity, 4);

        var start = Monday(1);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0201");
        Financials(engagement, 15000m, 46m);
        Scope(engagement,
            "cloud.anthropic.example", "Sottoscrizioni Azure",
            "Ambiente di produzione", "Lun-Ven 09:00-18:00",
            "cloud-security@anthropic.example",
            "Assessment su ambiente di pre-produzione", start.AddDays(-6));
        Credentials(engagement,
            "Non richiesta", "Service principal di sola lettura",
            "Azure Key Vault", "Nessuna password condivisa",
            start.AddDays(-6));
        Distribution(engagement);
        Proposal(engagement, "P-005", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione accettata");
        return engagement;
    }

    private static Engagement DoneCase()
    {
        var engagement = New(
            "ENG-2026-006", "nvidia", "Nvidia",
            "WAPT sito vetrina",
            EngagementType.PenetrationTest, 50);

        var start = Monday(-5);
        var end = Friday(start);
        var retestStart = Monday(-2);
        var retestEnd = Friday(retestStart);

        Docs(engagement, "PO-2026-0091");
        Financials(engagement, 20000m, 46m);
        Scope(engagement,
            "portal.nvidia.example", "10.50.10.0/24",
            "App mobile", "Lun-Ven 20:00-06:00",
            "appsec@nvidia.example", "Test completato", start.AddDays(-5));
        Credentials(engagement,
            "VPN-NOVA-01", "3 utenze di test",
            "Azure Key Vault", "Credenziali revocate a fine test",
            start.AddDays(-5));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Mail(engagement, "end", end);
        Mail(engagement, "deliver", end.AddDays(10));
        Proposal(engagement, "P-006", start, end, ProposalStatus.Accepted);
        Report(engagement, "R-006",
            "Report_ENG-2026-006_finale.txt", true, 21);
        engagement.ReviewStatus = ReviewStatus.Approved;
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Deliverable pubblicati");

        // Retest concluso: richiesto dal cliente, date approvate, report
        // inviato senza revisione. Caso demo del percorso completo.
        engagement.RetestRequestedDate = At(retestStart.AddDays(-5));
        engagement.RetestRequestedByName = "Giulia Sala";
        engagement.RetestProposals.Add(new Proposal
        {
            Id = "RE-01",
            By = "Luca Ferri",
            Kind = "retest",
            StartDate = retestStart,
            EndDate = retestEnd,
            Date = retestStart.AddDays(-4),
            Status = ProposalStatus.Accepted,
            Note = "Retest dopo i fix"
        });
        engagement.AcceptedRetestProposal = engagement.RetestProposals[0];
        engagement.RetestCompletedDate = At(retestEnd);
        engagement.RetestReport = new ReportFile(
            "R-006-RT",
            "Report_ENG-2026-006_retest.txt",
            "PDF",
            0,
            At(retestEnd),
            true,
            "Retest");
        engagement.RetestReportSentDate = At(retestEnd.AddDays(1));
        Mail(engagement, "retestreport", retestEnd.AddDays(1));
        Audit(engagement, "Sara Conti", Roles.Pentester,
            "Report di retest inviato al cliente (senza revisione)");
        return engagement;
    }

    private static Engagement DoneAnthropicCase()
    {
        var engagement = New(
            "ENG-2026-015", "anthropic", "Anthropic",
            "VA rete interna",
            EngagementType.VulnerabilityAssessment, 55);

        var start = Monday(-6);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0088");
        Financials(engagement, 7000m, 46m);
        Scope(engagement,
            "intranet.anthropic.example", "10.20.50.0/24",
            "Sistemi di produzione", "Lun-Ven 20:00-06:00",
            "security@anthropic.example", "Assessment concluso",
            start.AddDays(-5));
        Credentials(engagement,
            "VPN-ANTHROPIC-02", "2 utenze di test",
            "Azure Key Vault", "Credenziali revocate a fine test",
            start.AddDays(-5));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Mail(engagement, "end", end);
        Mail(engagement, "deliver", end.AddDays(10));
        Proposal(engagement, "P-015", start, end, ProposalStatus.Accepted);
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
            "VA configurazione M365", EngagementType.CloudSecurity, 5);

        var start = Monday(2);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0197");
        Financials(engagement, 11000m, 46m);
        Distribution(engagement);
        Proposal(engagement, "P-007", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Proposta di pianificazione accettata");
        return engagement;
    }

    private static Engagement CredentialsCase()
    {
        var engagement = New(
            "ENG-2026-008", "nvidia", "Nvidia",
            "CR API pagamenti", EngagementType.CodeReview, 8);

        var start = Monday(3);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0164");
        Financials(engagement, 14000m, 46m);
        Scope(engagement,
            "pay-api.nvidia.example", "10.20.40.0/24",
            "Produzione", "Lun-Ven 21:00-06:00",
            "appsec@nvidia.example",
            "Revisione dei servizi di pagamento", start.AddDays(-6));
        Distribution(engagement);
        Proposal(engagement, "P-008", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Scope completato, credenziali in attesa");
        SolicitationMail(engagement, "credentials", Today.AddDays(-3),
            string.Join("; ", NotificationContacts("nvidia")),
            "Sollecito: credenziali di accesso ENG-2026-008");
        return engagement;
    }

    private static Engagement PlanningCase()
    {
        var engagement = New(
            "ENG-2026-009", "anthropic", "Anthropic",
            "PT app mobile",
            EngagementType.PenetrationTest, 3);

        Docs(engagement, "PO-2026-0210");
        Financials(engagement, 16000m, 46m);
        Scope(engagement,
            "mobile.anthropic.example", "10.30.0.0/16",
            "Backend core", "Sempre attivo",
            "security@anthropic.example",
            "Test su build di pre-produzione", Today.AddDays(-2));
        Distribution(engagement);
        // Proposta di date del cliente in attesa del Manager: è il caso in cui
        // PMO e cliente possono sollecitare il Manager per la pianificazione.
        var start = Monday(2);
        Proposal(engagement, "P-009", start, Friday(start),
            ProposalStatus.Pending, ProposalParty.Client, "Marta Rinaldi",
            "Finestra proposta dal cliente");
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Scope completato, pianificazione da avviare");
        return engagement;
    }

    private static Engagement OtScheduledCase()
    {
        var engagement = New(
            "ENG-2026-010", "nvidia", "Nvidia",
            "VA rete OT",
            EngagementType.VulnerabilityAssessment, 15);

        var start = Monday(4);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0177");
        Financials(engagement, 24000m, 46m);
        Scope(engagement,
            "scada.nvidia.example", "10.60.0.0/16",
            "Rete corporate", "Notturno 22:00-06:00",
            "ot-security@nvidia.example",
            "Verifica della segmentazione rete OT", start.AddDays(-7));
        Credentials(engagement,
            "VPN-NVIDIA-OT", "4 utenze dedicate",
            "Vault aziendale",
            "Accesso limitato alla finestra notturna", start.AddDays(-7));
        Distribution(engagement);
        Proposal(engagement, "P-010", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO,
            "Pianificazione confermata");
        return engagement;
    }

    private static Engagement ReportingChangesCase()
    {
        var engagement = New(
            "ENG-2026-011", "anthropic", "Anthropic",
            "VA fornitore critico",
            EngagementType.VulnerabilityAssessment, 20);

        var start = Monday(-1);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0129");
        Financials(engagement, 13000m, 46m);
        Scope(engagement,
            "thirdparty.anthropic.example", "10.50.20.0/24",
            "Dati di produzione", "Lun-Ven 09:00-18:00",
            "vendorrisk@anthropic.example",
            "Assessment del fornitore critico", start.AddDays(-4));
        Credentials(engagement,
            "VPN-NOVA-03", "2 utenze fornitore",
            "File cifrato (password su canale separato)",
            "Credenziali condivise con il fornitore",
            start.AddDays(-4));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Mail(engagement, "end", end);
        Proposal(engagement, "P-011", start, end,
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
            "VA supply chain",
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
            "RT e-commerce", EngagementType.RedTeam, 10);

        // Red team: tre settimane, in corso.
        var start = Monday(0);
        var end = Friday(start, 3);

        Docs(engagement, "PO-2026-0194");
        Financials(engagement, 30000m, 46m);
        Scope(engagement,
            "shop.anthropic.example", "10.90.0.0/16",
            "Pagamenti", "Lun-Ven 22:00-06:00",
            "security@anthropic.example",
            "Red team sull'e-commerce", start.AddDays(-4));
        Credentials(engagement,
            "VPN-ANTHROPIC-01", "5 utenze di test",
            "Azure Key Vault", "Credenziali temporanee",
            start.AddDays(-4));
        Distribution(engagement);
        Mail(engagement, "start", start);
        Proposal(engagement, "P-013", start, end, ProposalStatus.Accepted);
        Audit(engagement, "Luca Ferri", Roles.PMO, "Engagement avviato");
        return engagement;
    }

    private static Engagement NvidiaScheduledCase()
    {
        var engagement = New(
            "ENG-2026-014", "nvidia", "Nvidia",
            "VA SAP S/4HANA",
            EngagementType.VulnerabilityAssessment, 18);

        var start = Monday(3);
        var end = Friday(start);

        Docs(engagement, "PO-2026-0151");
        Financials(engagement, 28000m, 46m);
        Scope(engagement,
            "sap.nvidia.example", "10.80.0.0/16",
            "Sistemi finanziari", "Lun-Ven 20:00-06:00",
            "sap-security@nvidia.example",
            "Assessment SAP S/4HANA", start.AddDays(-7));
        Credentials(engagement,
            "VPN-NVIDIA-01", "3 utenze di test",
            "Vault aziendale", "Accesso al mandante", start.AddDays(-7));
        Distribution(engagement);
        Proposal(engagement, "P-014", start, end, ProposalStatus.Accepted);
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
        DateOnly date)
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
            Date = At(date)
        };
    }

    private static void Credentials(
        Engagement engagement,
        string vpn,
        string users,
        string deliveryMethod,
        string notes,
        DateOnly date)
    {
        engagement.CredentialsDone = true;
        engagement.SecretReference = $"kv://demo/{engagement.Id}";
        engagement.Credentials = new CredentialData
        {
            Vpn = vpn,
            Users = users,
            DeliveryMethod = deliveryMethod,
            Notes = notes,
            Done = true,
            By = "Client User",
            Date = At(date)
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
            // La proposta nasce qualche giorno prima della finestra, mai nel
            // futuro.
            Date = start.AddDays(-5) > Today ? Today : start.AddDays(-5),
            Note = note,
            Status = status
        });
    }

    private static void Mail(Engagement engagement, string type, DateOnly when)
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
            At(when),
            string.Join(
                "; ",
                baseRecipients
                    .Concat(list.Select(entry => entry.Email))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
            true,
            "All"));
    }

    // Solleciti già partiti (registro demo): alimentano "Solleciti ricevuti"
    // nella pagina Solleciti, senza bloccare l'invio di oggi.
    private static void SolicitationMail(
        Engagement engagement,
        string taskKey,
        DateOnly when,
        string to,
        string subject)
    {
        engagement.MailLog.Add(new MailLogEntry(
            $"M-{engagement.Id}-{taskKey}-{when:yyyyMMdd}",
            SolicitationService.LogPrefix + taskKey,
            At(when),
            to,
            true,
            "All",
            subject));
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
