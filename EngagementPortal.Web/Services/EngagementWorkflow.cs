using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public sealed class EngagementWorkflow
{
    public EngagementPhase Resolve(
        Engagement engagement,
        DateOnly? currentDate = null)
    {
        var today = currentDate?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (!engagement.HasManleva ||!engagement.OrderConfirmed)
            return EngagementPhase.Docs;

        var proposal = engagement.AcceptedProposal;

        if (proposal is null)
            return EngagementPhase.Planning;

        if (!engagement.ScopeDone ||!engagement.CredentialsDone)
            return EngagementPhase.Prep;

        if (today < proposal.StartDate)
            return EngagementPhase.Scheduled;

        if (today <= proposal.EndDate)
            return EngagementPhase.Running;

        if (!engagement.Reports.Any(report => report.Published))
            return EngagementPhase.Reporting;

        return EngagementPhase.Done;
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
        var phase = workflow.Resolve(engagement);

        if (role == Roles.Manager &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.Pending)
            return new("Rivedi i deliverables", "reports");

        var pending = engagement.Proposals
            .FirstOrDefault(p => p.Status == ProposalStatus.Pending);

        var canDecide = pending is not null &&
            ((pending.From == ProposalParty.Client &&
              role == Roles.Cyberdefence) ||
             (pending.From == ProposalParty.Provider &&
              role == Roles.Client));

        if (canDecide)
            return new("Accetta o rifiuta la proposta", "plan");

        if (role == Roles.Client &&
            phase == EngagementPhase.Docs &&
            !engagement.HasManleva)
            return new("Firma e carica la manleva", "docs");

        if (role is Roles.Cybersec or Roles.Cyberdefence &&
            phase == EngagementPhase.Docs &&
            !engagement.OrderConfirmed)
            return new("Registra ordine", "docs");

        if (role is Roles.Client or Roles.Cybersec or Roles.Cyberdefence &&
            phase == EngagementPhase.Planning)
            return new("Proponi date", "plan");

        if (role is Roles.Client or Roles.Cyberdefence &&
            phase == EngagementPhase.Prep)
            return new("Fornisci scope e credenziali", "prep");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.Reports.Count == 0)
            return new("Carica deliverables", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.None)
            return new("Invia in revisione", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.Changes)
            return new("Aggiorna e reinvia", "reports");

        if (role == Roles.Cyberdefence &&
            phase == EngagementPhase.Reporting &&
            engagement.ReviewStatus == ReviewStatus.Approved)
            return new("Pubblica al cliente", "reports");

        return null;
    }
}
