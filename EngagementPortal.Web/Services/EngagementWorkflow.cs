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

        if (role == Roles.Manager &&
            engagement.ReviewStatus == ReviewStatus.Pending)
            return new TodoItem("Rivedi i deliverables", "reports");

        var pending = engagement.Proposals
            .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

        var canDecide = pending is not null &&
            ((pending.From == ProposalParty.Client &&
              role == Roles.Cyberdefence) ||
             (pending.From == ProposalParty.Provider &&
              role == Roles.Client));

        if (canDecide)
            return new TodoItem("Accetta o rifiuta la proposta", "plan");

        if (role == Roles.Client &&
            phase == EngagementPhase.Docs &&
            !engagement.HasManleva)
            return new TodoItem("Firma e carica la manleva", "docs");

        if (role is Roles.Cybersec or Roles.Cyberdefence &&
            phase == EngagementPhase.Docs &&
            !engagement.OrderConfirmed)
            return new TodoItem("Registra ordine", "docs");

        if (role is Roles.Client or Roles.Cybersec or Roles.Cyberdefence &&
            phase == EngagementPhase.Planning)
            return new TodoItem("Proponi date", "plan");

        if (role is Roles.Client or Roles.Cyberdefence &&
            phase == EngagementPhase.Prep)
        {
            if (!engagement.ScopeDone)
                return new TodoItem("Compila lo scope", "scope");

            if (!engagement.CredentialsDone)
                return new TodoItem("Inserisci le credenziali", "cred");

            return new TodoItem("Completa la preparazione", "scope");
        }

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            !engagement.Reports.Any(report => report.Published))
            return new TodoItem("Carica i deliverables", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.None)
            return new TodoItem("Invia in revisione", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.Changes)
            return new TodoItem("Aggiorna e reinvia", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.Approved)
            return new TodoItem("Pubblica al cliente", "reports");

        return null;
    }
}
