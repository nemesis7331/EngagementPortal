using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public sealed class EngagementWorkflow(IDateProvider clock)
{
    public DateOnly Today => clock.Today;

    public EngagementPhase Resolve(
        Engagement engagement,
        DateOnly? currentDate = null)
    {
        var today = currentDate ?? clock.Today;

        if (!engagement.HasManleva || !engagement.OrderConfirmed)
            return EngagementPhase.Docs;

        var proposal = engagement.AcceptedProposal;

        if (proposal is null)
            return EngagementPhase.Planning;

        // Precedenza allineata alla demo: un deliverable pubblicato chiude
        // l'engagement anche se le date non sono ancora trascorse.
        if (engagement.Reports.Any(report => report.Published))
            return EngagementPhase.Done;

        // Demo: oltre la data di fine si passa a reporting anche se la
        // preparazione non è completa.
        if (today > proposal.EndDate)
            return EngagementPhase.Reporting;

        if (!engagement.ScopeDone || !engagement.CredentialsDone)
            return EngagementPhase.Prep;

        if (today < proposal.StartDate)
            return EngagementPhase.Scheduled;

        return EngagementPhase.Running;
    }

    public static int StepIndex(EngagementPhase phase) => phase switch
    {
        EngagementPhase.Docs => 0,
        EngagementPhase.Planning => 1,
        EngagementPhase.Prep => 2,
        EngagementPhase.Scheduled or EngagementPhase.Running => 3,
        EngagementPhase.Reporting => 4,
        EngagementPhase.Done => 5,
        _ => 0
    };

    public static bool CanSeeCredentials(string role) =>
        role is Roles.Client or Roles.PMO or Roles.Pentester;

    public static bool CanEditPreparation(
        Engagement engagement,
        string role,
        EngagementPhase phase)
    {
        // Scope e credenziali si sbloccano appena i documenti sono completi:
        // si possono compilare anche prima che la pianificazione sia accettata.
        if (!engagement.HasManleva || !engagement.OrderConfirmed)
            return false;

        return role switch
        {
            Roles.Client => phase is EngagementPhase.Planning or
                EngagementPhase.Prep or
                EngagementPhase.Scheduled,
            Roles.PMO => phase is EngagementPhase.Planning or
                EngagementPhase.Prep or
                EngagementPhase.Scheduled or
                EngagementPhase.Running,
            _ => false
        };
    }
}

public sealed class TodoResolver(EngagementWorkflow workflow)
{
    public TodoItem? Resolve(
        Engagement engagement,
        string role,
        DateOnly? currentDate = null)
    {
        if (role == Roles.Auditor)
            return null;

        var phase = workflow.Resolve(engagement, currentDate);

        var pending = engagement.Proposals
            .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

        if (role == Roles.Manager)
        {
            return phase == EngagementPhase.Reporting &&
                   engagement.ReviewStatus == ReviewStatus.Pending
                ? new TodoItem(
                    "Rivedi i deliverables prima della pubblicazione.",
                    "reports")
                : null;
        }

        var canDecide = pending is not null &&
            ((pending.From == ProposalParty.Client &&
              role == Roles.PMO) ||
             (pending.From == ProposalParty.Provider &&
              role == Roles.Client));

        if (canDecide)
        {
            return new TodoItem(
                pending!.Kind == "resched"
                    ? "Richiesta di riprogrammazione: accetta o rifiuta."
                    : "Proposta di pianificazione: accetta o rifiuta.",
                "plan");
        }

        if (phase == EngagementPhase.Docs)
        {
            if (!engagement.HasManleva &&
                role is Roles.Client or Roles.PMO)
                return new TodoItem(
                    "Firma e carica la manleva",
                    "docs");

            if (!engagement.OrderConfirmed &&
                role is Roles.PMO)
                return new TodoItem("Registra ordine", "docs");

            return null;
        }

        if (phase == EngagementPhase.Planning && pending is null &&
            role is Roles.Client or Roles.PMO)
            return new TodoItem("Proponi date", "plan");

        if (phase == EngagementPhase.Prep)
        {
            var needsScope = !engagement.ScopeDone;
            var needsCredentials = !engagement.CredentialsDone &&
                role is Roles.Client or Roles.PMO;

            if (needsScope && needsCredentials)
                return new TodoItem(
                    "Fornisci scope e credenziali",
                    "scope");

            if (needsScope &&
                role is Roles.Client or Roles.PMO)
                return new TodoItem(
                    "Compila lo scope dell'attività",
                    "scope");

            if (needsCredentials)
                return new TodoItem(
                    "Fornisci le credenziali di accesso",
                    "cred");

            return null;
        }

        if (phase == EngagementPhase.Reporting &&
            role == Roles.Pentester)
        {
            if (!engagement.Reports.Any(report => !report.Published))
                return new TodoItem("Carica i deliverables", "reports");

            if (engagement.ReviewStatus == ReviewStatus.None)
                return new TodoItem("Invia in revisione", "reports");

            if (engagement.ReviewStatus == ReviewStatus.Changes)
                return new TodoItem("Aggiorna e reinvia", "reports");

            if (engagement.ReviewStatus == ReviewStatus.Approved)
                return new TodoItem("Pubblica al cliente", "reports");
        }

        return null;
    }

    /// <summary>
    /// Stato unico mostrato in lista, dettaglio e KPI. Il colore non viene
    /// deciso qui: la UI traduce <see cref="ActionState.Kind"/> nei token
    /// semantici (action, waiting, progress, done, blocked, neutral).
    /// </summary>
    public ActionState ResolveAction(
        Engagement engagement,
        string role,
        DateOnly? currentDate = null)
    {
        var today = currentDate ?? workflow.Today;
        var phase = workflow.Resolve(engagement, today);

        if (phase == EngagementPhase.Done)
            return new ActionState("done", null, null, "Completato", null, null);

        if (role == Roles.Auditor)
            return Waiting(engagement, phase, role);

        var todo = Resolve(engagement, role, today);

        if (todo is not null)
        {
            return new ActionState(
                "action",
                role,
                OwnerLabel(role),
                todo.Action,
                todo.TargetTab,
                Since(engagement, phase));
        }

        return Waiting(engagement, phase, role);
    }

    /// <summary>
    /// Stato delle schede del dettaglio, calcolato una sola volta lato server.
    /// </summary>
    public IReadOnlyDictionary<string, TabState> ResolveTabs(
        Engagement engagement,
        string role,
        EngagementPhase? currentPhase = null)
    {
        var phase = currentPhase ?? workflow.Resolve(engagement, workflow.Today);
        var todo = Resolve(engagement, role, workflow.Today);

        var tabs = new Dictionary<string, TabState>(StringComparer.Ordinal);

        tabs["docs"] = (engagement.HasManleva, engagement.OrderConfirmed) switch
        {
            (true, true) => new TabState("ok", null),
            _ when todo?.TargetTab == "docs" =>
                new TabState("todo", todo.Action),
            _ => new TabState("wait", !engagement.HasManleva
                ? "In attesa della firma del cliente"
                : "In attesa dell'ordine dal provider")
        };

        tabs["plan"] = !engagement.HasManleva || !engagement.OrderConfirmed
            ? new TabState("lock", "Si sblocca dopo firma e ordine")
            : engagement.AcceptedProposal is not null
                ? new TabState("ok", null)
                : todo?.TargetTab == "plan"
                    ? new TabState("todo", todo.Action)
                    : new TabState("wait", "In attesa di una proposta di date");

        var docsReady = engagement.HasManleva && engagement.OrderConfirmed;
        var canEditScope = EngagementWorkflow.CanEditPreparation(
            engagement, role, phase);

        tabs["scope"] = !docsReady
            ? new TabState("lock", "Si sblocca dopo firma e ordine")
            : engagement.ScopeDone
                ? new TabState("ok", null)
                : todo?.TargetTab == "scope"
                    ? new TabState("todo", todo.Action)
                    : canEditScope
                        ? new TabState(
                            "todo",
                            "Puoi compilarlo anche prima della pianificazione")
                        : new TabState("wait", "In attesa dello scope dal cliente");

        tabs["cred"] = !docsReady
            ? new TabState("lock", "Si sblocca dopo firma e ordine")
            : !EngagementWorkflow.CanSeeCredentials(role)
                ? new TabState("lock", "Non visibile al tuo profilo")
                : engagement.CredentialsDone
                    ? new TabState("ok", null)
                    : todo?.TargetTab == "cred"
                        ? new TabState("todo", todo.Action)
                        : canEditScope
                            ? new TabState(
                                "todo",
                                "Puoi compilarle anche prima della pianificazione")
                            : new TabState(
                                "wait",
                                "In attesa delle credenziali dal cliente");

        if (todo?.TargetTab == "reports")
        {
            tabs["reports"] = new TabState("todo", todo.Action);
        }
        else if (engagement.Reports.Any(report => report.Published))
        {
            tabs["reports"] = new TabState("ok", null);
        }
        else if (role == Roles.Client)
        {
            tabs["reports"] = phase == EngagementPhase.Reporting
                ? new TabState("wait", "In attesa di pubblicazione")
                : new TabState("lock", "Non ancora disponibili");
        }
        else if (engagement.Reports.Count > 0 ||
                 phase == EngagementPhase.Reporting)
        {
            tabs["reports"] = new TabState("wait", engagement.ReviewStatus switch
            {
                ReviewStatus.Pending => "In attesa della revisione del manager",
                ReviewStatus.Changes => "In attesa degli aggiornamenti dal provider",
                ReviewStatus.Approved => "In attesa di pubblicazione",
                _ => "In attesa di invio in revisione"
            });
        }
        else
        {
            tabs["reports"] = new TabState("lock", "Non ancora disponibili");
        }

        return tabs;
    }

    private ActionState Waiting(
        Engagement engagement,
        EngagementPhase phase,
        string role)
    {
        switch (phase)
        {
            case EngagementPhase.Docs:
                return !engagement.HasManleva
                    ? Wait(
                        Roles.Client,
                        OwnerLabel(Roles.Client),
                        "In attesa della firma della manleva dal cliente",
                        SinceDocs(engagement))
                    : Wait(
                        Roles.PMO,
                        OwnerLabel(Roles.PMO),
                        "In attesa dell'ordine dal provider",
                        SinceDocs(engagement));

            case EngagementPhase.Planning:
            {
                var pending = PendingProposal(engagement);

                if (pending is not null)
                {
                    var decider = pending.From == ProposalParty.Client
                        ? Roles.PMO
                        : Roles.Client;

                    return Wait(
                        decider,
                        OwnerLabel(decider),
                        decider == Roles.PMO
                            ? "In attesa della decisione del PMO"
                            : "In attesa della decisione del cliente",
                        pending.Date);
                }

                return Wait(
                    "Client/PMO",
                    "Cliente o PMO",
                    "In attesa di una proposta di date",
                    SinceDocs(engagement));
            }

            case EngagementPhase.Prep:
            {
                var missing = new List<string>();

                if (!engagement.ScopeDone)
                    missing.Add("scope");

                if (!engagement.CredentialsDone)
                    missing.Add("credenziali");

                return Wait(
                    Roles.Client,
                    OwnerLabel(Roles.Client),
                    $"In attesa di {string.Join(" e ", missing)} dal cliente",
                    SincePreparation(engagement));
            }

            case EngagementPhase.Scheduled:
                return new ActionState(
                    "progress",
                    null,
                    null,
                    engagement.AcceptedProposal is null
                        ? "Pianificato"
                        : $"Pianificato dal {engagement.AcceptedProposal.StartDate:dd/MM/yyyy}",
                    null,
                    null);

            case EngagementPhase.Running:
                return new ActionState(
                    "progress",
                    null,
                    null,
                    "In esecuzione",
                    null,
                    null);

            case EngagementPhase.Reporting:
                return ReportingWaiting(engagement);

            default:
                return new ActionState(
                    "neutral",
                    null,
                    null,
                    "Stato non disponibile",
                    null,
                    null);
        }
    }

    private static ActionState ReportingWaiting(Engagement engagement)
    {
        var since = LatestReportDate(engagement);

        if (engagement.ReviewStatus == ReviewStatus.Pending)
        {
            return Wait(
                Roles.Manager,
                OwnerLabel(Roles.Manager),
                "In attesa della revisione del manager",
                since);
        }

        if (engagement.ReviewStatus == ReviewStatus.Changes)
        {
            return Wait(
                Roles.Pentester,
                OwnerLabel(Roles.Pentester),
                "In attesa degli aggiornamenti dal provider",
                since);
        }

        if (engagement.ReviewStatus == ReviewStatus.Approved &&
            engagement.Reports.Any(report => !report.Published))
        {
            return Wait(
                Roles.Pentester,
                OwnerLabel(Roles.Pentester),
                "In attesa della pubblicazione dal provider",
                since);
        }

        if (engagement.Reports.Count == 0)
        {
            return Wait(
                Roles.Pentester,
                OwnerLabel(Roles.Pentester),
                "In attesa dei deliverables dal provider",
                since);
        }

        return new ActionState(
            "progress",
            null,
            null,
            "Report in redazione",
            null,
            null);
    }

    private static ActionState Wait(
        string owner,
        string ownerLabel,
        string label,
        DateOnly since) =>
        new("waiting", owner, ownerLabel, label, null, since);

    private static DateOnly? Since(
        Engagement engagement,
        EngagementPhase phase) => phase switch
    {
        EngagementPhase.Docs => SinceDocs(engagement),
        EngagementPhase.Planning =>
            PendingProposal(engagement) is { } pending
                ? pending.Date
                : SinceDocs(engagement),
        EngagementPhase.Prep => SincePreparation(engagement),
        EngagementPhase.Reporting => LatestReportDate(engagement),
        _ => null
    };

    private static DateOnly SinceDocs(Engagement engagement) =>
        DateOnly.FromDateTime(engagement.CreatedDate.UtcDateTime);

    private static DateOnly SincePreparation(Engagement engagement) =>
        engagement.AcceptedProposal is null
            ? SinceDocs(engagement)
            : engagement.AcceptedProposal.Date;

    private static DateOnly LatestReportDate(Engagement engagement) =>
        engagement.Reports.Count > 0
            ? DateOnly.FromDateTime(
                engagement.Reports.Max(report => report.Date).UtcDateTime)
            : engagement.AcceptedProposal?.EndDate ?? SinceDocs(engagement);

    private static Proposal? PendingProposal(Engagement engagement) =>
        engagement.Proposals
            .FirstOrDefault(proposal =>
                proposal.Status == ProposalStatus.Pending);

    private static string OwnerLabel(string role) => role switch
    {
        Roles.Client => "Cliente",
        Roles.PMO => "PMO",
        Roles.Pentester => "Provider",
        Roles.Manager => "Manager",
        _ => role
    };
}

public sealed record ActionState(
    string Kind,
    string? Owner,
    string? OwnerLabel,
    string Label,
    string? TargetTab,
    DateOnly? Since);

public sealed record TabState(string State, string? Reason);
