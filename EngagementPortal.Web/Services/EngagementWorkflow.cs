using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public sealed class EngagementWorkflow(IDateProvider clock)
{
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
}

public sealed class TodoResolver(EngagementWorkflow workflow)
{
    public TodoItem? Resolve(Engagement engagement, string role)
    {
        if (role == Roles.Auditor)
            return null;

        var phase = workflow.Resolve(engagement);

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
}
