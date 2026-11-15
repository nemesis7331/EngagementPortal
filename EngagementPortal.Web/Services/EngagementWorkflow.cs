using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public sealed class EngagementWorkflow(IDateProvider clock)
{
    public DateOnly Today => clock.Today;

    /// <summary>
    /// Segnala la preparazione incompleta (scope/credenziali) a ridosso
    /// dell'esecuzione: l'allerta scatta da 14 giorni prima della data di
    /// inizio e resta finché i dati mancano. Null se non c'è nulla da
    /// segnalare o se l'engagement è concluso.
    /// </summary>
    public static PreparationAlert? PreparationAlertFor(
        Engagement engagement,
        EngagementPhase phase,
        DateOnly today)
    {
        if (phase is EngagementPhase.Setup or EngagementPhase.Done or
            EngagementPhase.Retest or EngagementPhase.RetestReporting)
            return null;

        var start = engagement.AcceptedProposal?.StartDate;

        if (start is null)
            return null;

        var missing = new List<string>();

        if (!engagement.ScopeDone)
            missing.Add("scope");

        if (!engagement.CredentialsDone)
            missing.Add("credenziali");

        if (missing.Count == 0)
            return null;

        var days = start.Value.DayNumber - today.DayNumber;

        // 14 giorni prima dell'avvio (o meno): preparazione a rischio.
        return days <= 14
            ? new PreparationAlert("late", missing, days)
            : null;
    }

    public EngagementPhase Resolve(
        Engagement engagement,
        DateOnly? currentDate = null)
    {
        var today = currentDate ?? clock.Today;

        if (!engagement.SetupDone)
            return EngagementPhase.Setup;

        if (!DocsComplete(engagement))
            return EngagementPhase.Docs;

        var proposal = engagement.AcceptedProposal;

        // Precedenza allineata alla demo: un deliverable pubblicato chiude la
        // parte principale; se il recheck è previsto si passa al retest.
        if (engagement.Reports.Any(report => report.Published))
            return RetestPhase(engagement);

        // Oltre la data di fine si passa a reporting anche se la
        // preparazione non è completa.
        if (proposal is not null && today > proposal.EndDate)
            return EngagementPhase.Reporting;

        // Ordine del workflow: documenti, scope, pianificazione,
        // credenziali, esecuzione, report.
        if (!engagement.ScopeDone)
            return EngagementPhase.Scope;

        if (proposal is null)
            return EngagementPhase.Planning;

        if (!engagement.CredentialsDone)
            return EngagementPhase.Credentials;

        if (today < proposal.StartDate)
            return EngagementPhase.Scheduled;

        return EngagementPhase.Running;
    }

    /// <summary>
    /// Dopo la pubblicazione del report: se il recheck è previsto si passa al
    /// retest (richiesta del cliente, date, esecuzione) e all'invio del report
    /// di retest, senza passare dalla revisione del Manager. Altrimenti
    /// l'attività è conclusa.
    /// </summary>
    private static EngagementPhase RetestPhase(Engagement engagement)
    {
        if (engagement.RetestReportSentDate is not null)
            return EngagementPhase.Done;

        // Report caricato (o retest eseguito): resta solo da inviarlo.
        if (engagement.RetestReport is not null ||
            engagement.RetestCompletedDate is not null)
            return EngagementPhase.RetestReporting;

        return engagement.RetestPlanned ||
            engagement.RetestRequestedDate is not null ||
            engagement.AcceptedRetestProposal is not null
            ? EngagementPhase.Retest
            : EngagementPhase.Done;
    }

    public static int StepIndex(EngagementPhase phase) => phase switch
    {
        EngagementPhase.Setup => 0,
        EngagementPhase.Docs => 1,
        EngagementPhase.Scope => 2,
        EngagementPhase.Planning => 3,
        EngagementPhase.Credentials => 4,
        EngagementPhase.Scheduled or EngagementPhase.Running => 5,
        EngagementPhase.Reporting => 6,
        EngagementPhase.Retest => 7,
        EngagementPhase.RetestReporting => 8,
        EngagementPhase.Done => 9,
        _ => 0
    };

    public static bool CanSeeCredentials(string role) =>
        role is Roles.Client or Roles.PMO or Roles.Pentester or Roles.Manager;

    /// <summary>
    /// Il primo passo è completo con manleva, ordine e costi/margine.
    /// </summary>
    public static bool DocsComplete(Engagement engagement) =>
        engagement.HasManleva &&
        engagement.OrderConfirmed &&
        engagement.FinancialsDone &&
        engagement.FinancialsApproved;

    public static bool CanEditFinancials(
        Engagement engagement,
        string role,
        EngagementPhase phase) =>
        phase == EngagementPhase.Docs &&
        role is Roles.PMO or Roles.Manager;

    /// <summary>
    /// Costi e margine sono visibili solo a PMO e Manager: gli altri profili
    /// non ricevono i campi economici nemmeno in lettura.
    /// </summary>
    public static bool CanViewFinancials(string role) =>
        role is Roles.PMO or Roles.Manager;

    public static bool CanApproveFinancials(
        Engagement engagement,
        string role,
        EngagementPhase phase) =>
        phase == EngagementPhase.Docs &&
        role == Roles.Manager &&
        engagement.FinancialsDone &&
        !engagement.FinancialsApproved;

    public static bool CanEditPreparation(
        Engagement engagement,
        string role,
        EngagementPhase phase)
    {
        // Lo scope si sblocca appena il primo passo è completo.
        if (!DocsComplete(engagement))
            return false;

        return role switch
        {
            Roles.Client => phase is EngagementPhase.Scope or
                EngagementPhase.Planning or
                EngagementPhase.Credentials or
                EngagementPhase.Scheduled,
            Roles.PMO => phase is EngagementPhase.Scope or
                EngagementPhase.Planning or
                EngagementPhase.Credentials or
                EngagementPhase.Scheduled or
                EngagementPhase.Running,
            _ => false
        };
    }

    /// <summary>
    /// Le credenziali seguono lo scope per Cliente e PMO; in più il Manager
    /// può vederle e modificarle nelle fasi operative, senza poter toccare
    /// scope o pianificazione.
    /// </summary>
    public static bool CanEditCredentials(
        Engagement engagement,
        string role,
        EngagementPhase phase)
    {
        if (!DocsComplete(engagement))
            return false;

        return role switch
        {
            Roles.Client => phase is EngagementPhase.Scope or
                EngagementPhase.Planning or
                EngagementPhase.Credentials or
                EngagementPhase.Scheduled,
            Roles.PMO => phase is EngagementPhase.Scope or
                EngagementPhase.Planning or
                EngagementPhase.Credentials or
                EngagementPhase.Scheduled or
                EngagementPhase.Running,
            Roles.Manager => phase is not EngagementPhase.Done,
            _ => false
        };
    }

    /// <summary>
    /// Giorni minimi tra la richiesta di engagement e la proposta di
    /// pianificazione. È un avviso, non un blocco: cliente e PMO possono
    /// comunque inviare la proposta.
    /// </summary>
    public const int PlanningNoticeDays = 14;

    public static ProposalNotice? PlanningNotice(
        Engagement engagement,
        DateOnly today)
    {
        var requestDate = DateOnly.FromDateTime(
            engagement.CreatedDate.UtcDateTime);
        var availableFrom = requestDate.AddDays(PlanningNoticeDays);

        if (today >= availableFrom)
            return null;

        return new ProposalNotice(
            requestDate,
            availableFrom,
            availableFrom.DayNumber - today.DayNumber);
    }
}

public sealed record ProposalNotice(
    DateOnly RequestDate,
    DateOnly AvailableFrom,
    int DaysRemaining);

public sealed record PreparationAlert(
    string Level,
    IReadOnlyList<string> Missing,
    int DaysToStart);

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
        var today = currentDate ?? workflow.Today;

        // Retest e report di retest valgono per tutti i profili: il cliente
        // chiede, il Manager approva le date, il Pentester esegue e invia.
        if (phase == EngagementPhase.Retest)
        {
            if (engagement.RetestRequestedDate is null)
            {
                return role == Roles.Client
                    ? new TodoItem(
                        "Richiedi il retest a fix completati",
                        "reports")
                    : null;
            }

            var pendingRetest = engagement.RetestProposals
                .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

            if (pendingRetest is not null)
            {
                return role == Roles.Manager
                    ? new TodoItem(
                        "Date del retest: approva o rifiuta.",
                        "reports")
                    : null;
            }

            if (engagement.AcceptedRetestProposal is null)
            {
                return role is Roles.PMO or Roles.Client
                    ? new TodoItem("Proponi le date del retest", "reports")
                    : null;
            }

            if (today < engagement.AcceptedRetestProposal.StartDate)
                return null;

            return role == Roles.Pentester
                ? new TodoItem(
                    "Concludi il retest e carica il report",
                    "reports")
                : null;
        }

        if (phase == EngagementPhase.RetestReporting)
        {
            if (role != Roles.Pentester)
                return null;

            if (engagement.RetestReport is null)
                return new TodoItem("Carica il report di retest", "reports");

            return engagement.RetestReportSentDate is null
                ? new TodoItem(
                    "Invia il report di retest al cliente (senza revisione)",
                    "reports")
                : null;
        }

        var pending = engagement.Proposals
            .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

        if (phase == EngagementPhase.Setup)
        {
            return role is Roles.Manager or Roles.PMO
                ? new TodoItem(
                    "Completa il setup: team, liste, lingua e deliverable",
                    "setup")
                : null;
        }

        // Le proposte di pianificazione possono essere inviate da cliente o PMO,
        // ma solo il Manager le approva o le rifiuta.
        if (role == Roles.Manager)
        {
            if (phase == EngagementPhase.Docs)
            {
                if (!engagement.FinancialsDone)
                    return new TodoItem("Inserisci costi e margine", "docs");

                if (!engagement.FinancialsApproved)
                    return new TodoItem("Approva costi e margine", "docs");
            }

            if (phase == EngagementPhase.Reporting &&
                engagement.ReviewStatus == ReviewStatus.Pending)
            {
                return new TodoItem(
                    "Rivedi i deliverables prima della pubblicazione.",
                    "reports");
            }

            if (pending is not null)
            {
                return new TodoItem(
                    pending.Kind == "resched"
                        ? "Richiesta di riprogrammazione: approva o rifiuta."
                        : "Proposta di pianificazione: approva o rifiuta.",
                    "plan");
            }

            return null;
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

            if (!engagement.FinancialsDone &&
                role is Roles.PMO)
                return new TodoItem("Inserisci costi e margine", "docs");

            return null;
        }

        if (phase == EngagementPhase.Scope && !engagement.ScopeDone &&
            role is Roles.Client or Roles.PMO)
            return new TodoItem("Compila lo scope dell'attività", "scope");

        if (phase == EngagementPhase.Planning && pending is null &&
            role is Roles.Client or Roles.PMO)
            return new TodoItem("Proponi date", "plan");

        if (phase == EngagementPhase.Credentials &&
            !engagement.CredentialsDone &&
            role is Roles.Client or Roles.PMO)
            return new TodoItem("Fornisci le credenziali di accesso", "cred");

        if (phase == EngagementPhase.Reporting &&
            role == Roles.Pentester)
        {
            var missingTypes = engagement.MissingReportTypes;
            if (missingTypes.Count > 0)
                return new TodoItem(
                    $"Carica i deliverable mancanti: {string.Join(", ", missingTypes)}",
                    "reports");

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
            return Waiting(engagement, phase, role, today);

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

        return Waiting(engagement, phase, role, today);
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

        tabs["setup"] = engagement.SetupDone
            ? new TabState("ok", null)
            : todo?.TargetTab == "setup"
                ? new TabState("todo", todo.Action)
                : role is Roles.Manager or Roles.PMO
                    ? new TabState(
                        "todo",
                        "Definisci team, liste, lingua e deliverable")
                    : new TabState("wait", "In attesa del setup dal PMO");

        tabs["docs"] = !engagement.SetupDone
            ? new TabState("lock", "Si sblocca dopo il setup iniziale")
            : EngagementWorkflow.DocsComplete(engagement)
            ? new TabState("ok", null)
            : todo?.TargetTab == "docs"
                ? new TabState("todo", todo.Action)
                : new TabState("wait", !engagement.HasManleva
                    ? "In attesa della firma del cliente"
                    : !engagement.OrderConfirmed
                        ? "In attesa che il PMO registri l'ordine"
                        : !engagement.FinancialsDone
                            ? "In attesa di costi e margine da PMO o Manager"
                            : !engagement.FinancialsApproved
                                ? "In attesa dell'approvazione del Manager"
                                : "In attesa del completamento della prima fase");

        var pendingProposal = engagement.Proposals
            .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

        tabs["plan"] = !engagement.SetupDone || !EngagementWorkflow.DocsComplete(engagement)
            ? new TabState("lock", "Si sblocca dopo il setup, firma, ordine, costi e approvazione")
            : todo?.TargetTab == "plan"
                ? new TabState("todo", todo.Action)
                : pendingProposal is not null
                    ? new TabState(
                        "wait",
                        "In attesa dell'approvazione del Manager")
                    : engagement.AcceptedProposal is not null
                        ? new TabState("ok", null)
                        : new TabState("wait", "In attesa di una proposta di date");

        var docsReady = engagement.SetupDone &&
            EngagementWorkflow.DocsComplete(engagement);
        var canEditPreparation = EngagementWorkflow.CanEditPreparation(
            engagement, role, phase);
        var canEditCredentials = EngagementWorkflow.CanEditCredentials(
            engagement, role, phase);

        tabs["scope"] = !docsReady
            ? new TabState("lock", "Si sblocca dopo setup, firma, ordine, costi e approvazione")
            : engagement.ScopeDone
                ? new TabState("ok", null)
                : todo?.TargetTab == "scope"
                    ? new TabState("todo", todo.Action)
                    : canEditPreparation
                        ? new TabState(
                            "todo",
                            "Compilabile subito dopo firma e ordine")
                        : new TabState("wait", "In attesa dello scope dal cliente");

        tabs["cred"] = !docsReady
            ? new TabState("lock", "Si sblocca dopo setup, firma, ordine, costi e approvazione")
            : !EngagementWorkflow.CanSeeCredentials(role)
                ? new TabState("lock", "Non visibile al tuo profilo")
                : engagement.CredentialsDone
                    ? new TabState("ok", null)
                    : todo?.TargetTab == "cred"
                        ? new TabState("todo", todo.Action)
                        : canEditCredentials
                            ? new TabState(
                                "todo",
                                "Compilabile anche durante la pianificazione")
                            : new TabState(
                                "wait",
                                "In attesa delle credenziali dal cliente");

        if (!engagement.SetupDone)
        {
            tabs["reports"] = new TabState(
                "lock",
                "Si sblocca dopo il setup iniziale");
        }
        else if (todo?.TargetTab == "reports")
        {
            tabs["reports"] = new TabState("todo", todo.Action);
        }
        else if (phase is EngagementPhase.Retest or EngagementPhase.RetestReporting)
        {
            // La parte principale è pubblicata: la scheda Report segue il retest.
            var state = ResolveAction(engagement, role, workflow.Today);
            tabs["reports"] = new TabState(
                state.Kind == "waiting" ? "wait" : "progress",
                state.Label);
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
                ReviewStatus.Changes => "In attesa degli aggiornamenti dal Pentester",
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
        string role,
        DateOnly today)
    {
        // Una riprogrammazione in attesa di approvazione riguarda il Manager
        // anche quando l'attività è già pianificata o in corso.
        if ((phase is EngagementPhase.Scope or
                EngagementPhase.Credentials or
                EngagementPhase.Scheduled or
                EngagementPhase.Running) &&
            PendingProposal(engagement) is { } reschedule)
        {
            return Wait(
                Roles.Manager,
                OwnerLabel(Roles.Manager),
                "In attesa dell'approvazione della riprogrammazione",
                reschedule.Date);
        }

        switch (phase)
        {
            case EngagementPhase.Setup:
                return Wait(
                    "PMO/Manager",
                    "PMO o Manager",
                    "In attesa del setup iniziale",
                    SinceDocs(engagement));

            case EngagementPhase.Docs:
                if (!engagement.HasManleva)
                {
                    return Wait(
                        Roles.Client,
                        OwnerLabel(Roles.Client),
                        "In attesa della firma della manleva dal cliente",
                        SinceDocs(engagement));
                }

                if (!engagement.OrderConfirmed)
                {
                    return Wait(
                        Roles.PMO,
                        OwnerLabel(Roles.PMO),
                        "In attesa che il PMO registri l'ordine",
                        SinceDocs(engagement));
                }

                if (!engagement.FinancialsDone)
                {
                    return Wait(
                        "PMO/Manager",
                        "PMO o Manager",
                        "In attesa di costi e margine",
                        SinceDocs(engagement));
                }

                return Wait(
                    Roles.Manager,
                    OwnerLabel(Roles.Manager),
                    "In attesa dell'approvazione di costi e margine",
                    SinceDocs(engagement));

            case EngagementPhase.Planning:
            {
                var pending = PendingProposal(engagement);

                if (pending is not null)
                {
                    return Wait(
                        Roles.Manager,
                        OwnerLabel(Roles.Manager),
                        "In attesa dell'approvazione del Manager",
                        pending.Date);
                }

                return Wait(
                    "Client/PMO",
                    "Cliente o PMO",
                    "In attesa di una proposta di date",
                    SinceDocs(engagement));
            }

            case EngagementPhase.Scope:
                return Wait(
                    Roles.Client,
                    OwnerLabel(Roles.Client),
                    "In attesa dello scope dal cliente",
                    SincePreparation(engagement));

            case EngagementPhase.Credentials:
                return Wait(
                    Roles.Client,
                    OwnerLabel(Roles.Client),
                    "In attesa delle credenziali dal cliente",
                    SincePreparation(engagement));

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

            case EngagementPhase.Retest:
                return RetestWaiting(engagement, today);

            case EngagementPhase.RetestReporting:
                return Wait(
                    Roles.Pentester,
                    OwnerLabel(Roles.Pentester),
                    engagement.RetestReport is null
                        ? "In attesa del report di retest dal Pentester"
                        : "In attesa dell'invio del report di retest al cliente",
                    SinceRetestReport(engagement));

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
                "In attesa degli aggiornamenti dal Pentester",
                since);
        }

        if (engagement.ReviewStatus == ReviewStatus.Approved &&
            engagement.Reports.Any(report => !report.Published))
        {
            return Wait(
                Roles.Pentester,
                OwnerLabel(Roles.Pentester),
                "In attesa della pubblicazione dal Pentester",
                since);
        }

        if (engagement.Reports.Count == 0)
        {
            return Wait(
                Roles.Pentester,
                OwnerLabel(Roles.Pentester),
                "In attesa dei deliverables dal Pentester",
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

    /// <summary>Attese del retest: richiesta del cliente, date, esecuzione.</summary>
    private static ActionState RetestWaiting(
        Engagement engagement,
        DateOnly today)
    {
        if (engagement.RetestRequestedDate is null)
        {
            return Wait(
                Roles.Client,
                OwnerLabel(Roles.Client),
                "In attesa della richiesta di retest dal cliente",
                SinceRetest(engagement));
        }

        var pending = engagement.RetestProposals
            .FirstOrDefault(proposal => proposal.Status == ProposalStatus.Pending);

        if (pending is not null)
        {
            return Wait(
                Roles.Manager,
                OwnerLabel(Roles.Manager),
                "In attesa dell'approvazione delle date del retest",
                pending.Date);
        }

        if (engagement.AcceptedRetestProposal is not { } accepted)
        {
            return Wait(
                "Client/PMO",
                "Cliente o PMO",
                "In attesa delle date del retest",
                SinceRetest(engagement));
        }

        // Il Pentester ha l'azione "concludi il retest" dal giorno di avvio;
        // qui restano gli altri profili, che vedono solo l'avanzamento.
        if (today < accepted.StartDate)
        {
            return new ActionState(
                "progress",
                null,
                null,
                $"Retest pianificato dal {accepted.StartDate:dd/MM/yyyy}",
                null,
                null);
        }

        return new ActionState(
            "progress",
            null,
            null,
            today > accepted.EndDate
                ? "Retest da concludere"
                : "Retest in corso",
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
        EngagementPhase.Setup => SinceDocs(engagement),
        EngagementPhase.Docs => SinceDocs(engagement),
        EngagementPhase.Planning =>
            PendingProposal(engagement) is { } pending
                ? pending.Date
                : SinceDocs(engagement),
        EngagementPhase.Scope or EngagementPhase.Credentials =>
            SincePreparation(engagement),
        EngagementPhase.Reporting => LatestReportDate(engagement),
        EngagementPhase.Retest => SinceRetest(engagement),
        EngagementPhase.RetestReporting => SinceRetestReport(engagement),
        _ => null
    };

    private static DateOnly SinceRetest(Engagement engagement) =>
        engagement.RetestRequestedDate is { } requested
            ? DateOnly.FromDateTime(requested.UtcDateTime)
            : engagement.AcceptedRetestProposal?.Date ?? SinceDocs(engagement);

    private static DateOnly SinceRetestReport(Engagement engagement) =>
        engagement.RetestCompletedDate is { } completed
            ? DateOnly.FromDateTime(completed.UtcDateTime)
            : SinceRetest(engagement);

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
        Roles.Pentester => "Pentester",
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
