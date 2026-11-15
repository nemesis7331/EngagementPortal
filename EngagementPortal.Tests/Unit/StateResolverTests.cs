using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public sealed class StateResolverTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private sealed class FixedDateProvider : IDateProvider
    {
        public DateOnly Today => StateResolverTests.Today;

        public DateTimeOffset UtcNow =>
            new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static EngagementWorkflow Workflow() =>
        new(new FixedDateProvider());

    private static TodoResolver Resolver() => new(Workflow());

    private static Engagement NewEngagement() => new()
    {
        Id = "ENG-2026-001",
        ClientId = "client",
        ClientName = "Acme",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Provider Pentester",
        CreatedDate = new DateTimeOffset(
            Today.AddDays(-2).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero),
        HasManleva = true,
        OrderConfirmed = true,
        Cost = 12000m,
        Margin = 46m,
        FinancialsApproved = true,
        ScopeDone = true,
        CredentialsDone = true,
        ReachabilityVerified = true
    };

    private static void AddAcceptedProposal(
        Engagement engagement,
        DateOnly start,
        DateOnly end)
    {
        engagement.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Provider",
            StartDate = start,
            EndDate = end,
            Date = Today.AddDays(-1),
            Status = ProposalStatus.Accepted
        });
    }

    private static void AddPendingProposal(
        Engagement engagement,
        ProposalParty from,
        string kind = "initial")
    {
        engagement.Proposals.Add(new Proposal
        {
            Id = "P-002",
            From = from,
            By = from == ProposalParty.Client ? "Cliente" : "Provider",
            Kind = kind,
            StartDate = Today.AddDays(2),
            EndDate = Today.AddDays(5),
            Date = Today,
            Status = ProposalStatus.Pending
        });
    }

    private static void AddReport(
        Engagement engagement,
        bool published)
    {
        engagement.Reports.Add(new ReportFile(
            "R-001",
            "report.pdf",
            "PDF",
            1024,
            new DateTimeOffset(
                Today.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero),
            published));
    }

    [Fact]
    public void MissingManleva_ClientHasAction_PentesterWaits()
    {
        var engagement = NewEngagement();
        engagement.HasManleva = false;

        var client = Resolver().ResolveAction(engagement, Roles.Client);
        var pentester = Resolver().ResolveAction(engagement, Roles.Pentester);

        Assert.Equal("action", client.Kind);
        Assert.Equal(Roles.Client, client.Owner);
        Assert.Equal("docs", client.TargetTab);

        Assert.Equal("waiting", pentester.Kind);
        Assert.Equal(Roles.Client, pentester.Owner);
        Assert.Contains("manleva", pentester.Label);
    }

    [Fact]
    public void MissingOrderPmoHasAction_ClientWaits()
    {
        var engagement = NewEngagement();
        engagement.OrderConfirmed = false;

        var pmo = Resolver().ResolveAction(engagement, Roles.PMO);
        var client = Resolver().ResolveAction(engagement, Roles.Client);

        Assert.Equal("action", pmo.Kind);
        Assert.Equal("docs", pmo.TargetTab);
        Assert.Equal("Registra ordine", pmo.Label);

        Assert.Equal("waiting", client.Kind);
        Assert.Equal(Roles.PMO, client.Owner);
    }

    [Fact]
    public void PendingProposalFromProvider_OnlyManagerDecides()
    {
        var engagement = NewEngagement();
        AddPendingProposal(engagement, ProposalParty.Provider);

        var manager = Resolver().ResolveAction(engagement, Roles.Manager);
        var client = Resolver().ResolveAction(engagement, Roles.Client);
        var pmo = Resolver().ResolveAction(engagement, Roles.PMO);

        Assert.Equal("action", manager.Kind);
        Assert.Equal(Roles.Manager, manager.Owner);
        Assert.Equal("plan", manager.TargetTab);
        Assert.Contains("approva", manager.Label);

        Assert.Equal("waiting", client.Kind);
        Assert.Equal(Roles.Manager, client.Owner);

        Assert.Equal("waiting", pmo.Kind);
        Assert.Equal(Roles.Manager, pmo.Owner);
    }

    [Fact]
    public void PendingProposalFromClient_OnlyManagerDecides()
    {
        var engagement = NewEngagement();
        AddPendingProposal(engagement, ProposalParty.Client);

        var manager = Resolver().ResolveAction(engagement, Roles.Manager);
        var pmo = Resolver().ResolveAction(engagement, Roles.PMO);
        var client = Resolver().ResolveAction(engagement, Roles.Client);

        Assert.Equal("action", manager.Kind);
        Assert.Equal("plan", manager.TargetTab);

        Assert.Equal("waiting", pmo.Kind);
        Assert.Equal(Roles.Manager, pmo.Owner);
        Assert.Contains("Manager", pmo.Label);

        Assert.Equal("waiting", client.Kind);
        Assert.Equal(Roles.Manager, client.Owner);
    }

    [Fact]
    public void PendingReschedule_ManagerDecidesDuringExecution()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-1), Today.AddDays(3));
        AddPendingProposal(engagement, ProposalParty.Client, "resched");

        var manager = Resolver().ResolveAction(engagement, Roles.Manager);
        var pmo = Resolver().ResolveAction(engagement, Roles.PMO);

        Assert.Equal("action", manager.Kind);
        Assert.Equal("plan", manager.TargetTab);
        Assert.Contains("riprogrammazione", manager.Label);

        Assert.Equal("waiting", pmo.Kind);
        Assert.Equal(Roles.Manager, pmo.Owner);
    }

    [Fact]
    public void ReviewPending_ManagerActsPentesterWaits()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        AddReport(engagement, published: false);
        engagement.ReviewStatus = ReviewStatus.Pending;

        var manager = Resolver().ResolveAction(engagement, Roles.Manager);
        var pentester = Resolver().ResolveAction(engagement, Roles.Pentester);

        Assert.Equal("action", manager.Kind);
        Assert.Equal("reports", manager.TargetTab);

        Assert.Equal("waiting", pentester.Kind);
        Assert.Equal(Roles.Manager, pentester.Owner);
    }

    [Fact]
    public void ReviewChanges_PentesterHasAction()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        AddReport(engagement, published: false);
        engagement.ReviewStatus = ReviewStatus.Changes;

        var pentester = Resolver().ResolveAction(engagement, Roles.Pentester);

        Assert.Equal("action", pentester.Kind);
        Assert.Equal("Aggiorna e reinvia", pentester.Label);
        Assert.Equal("reports", pentester.TargetTab);
    }

    [Fact]
    public void ReviewApproved_PentesterPublishesManagerWaits()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        AddReport(engagement, published: false);
        engagement.ReviewStatus = ReviewStatus.Approved;

        var pentester = Resolver().ResolveAction(engagement, Roles.Pentester);
        var manager = Resolver().ResolveAction(engagement, Roles.Manager);

        Assert.Equal("action", pentester.Kind);
        Assert.Equal("Pubblica al cliente", pentester.Label);

        Assert.Equal("waiting", manager.Kind);
        Assert.Equal(Roles.Pentester, manager.Owner);
    }

    [Fact]
    public void PublishedReport_IsDone()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        AddReport(engagement, published: true);

        var state = Resolver().ResolveAction(engagement, Roles.Client);

        Assert.Equal("done", state.Kind);
        Assert.Null(state.Owner);
    }

    [Fact]
    public void Tabs_DocsWaitsForFinancialApproval()
    {
        var engagement = NewEngagement();
        engagement.FinancialsApproved = false;

        var manager = Resolver().ResolveTabs(engagement, Roles.Manager);
        var pmo = Resolver().ResolveTabs(engagement, Roles.PMO);
        var client = Resolver().ResolveTabs(engagement, Roles.Client);

        Assert.Equal("todo", manager["docs"].State);
        Assert.Contains("Approva", manager["docs"].Reason);
        Assert.Equal("wait", pmo["docs"].State);
        Assert.Contains("approvazione", pmo["docs"].Reason);
        Assert.Equal("wait", client["docs"].State);
        Assert.Contains("approvazione", client["docs"].Reason);
        Assert.Equal("lock", client["plan"].State);
    }

    [Fact]
    public void Tabs_DocsWaitsForFinancials()
    {
        var engagement = NewEngagement();
        engagement.Cost = null;
        engagement.Margin = null;

        var pmo = Resolver().ResolveTabs(engagement, Roles.PMO);
        var manager = Resolver().ResolveTabs(engagement, Roles.Manager);
        var client = Resolver().ResolveTabs(engagement, Roles.Client);

        Assert.Equal("todo", pmo["docs"].State);
        Assert.Equal("todo", manager["docs"].State);
        Assert.Equal("wait", client["docs"].State);
        Assert.Contains("costi", client["docs"].Reason);
        Assert.Equal("lock", client["plan"].State);
    }

    [Fact]
    public void Tabs_PlanIsLockedUntilDocsAreComplete()
    {
        var engagement = NewEngagement();
        engagement.HasManleva = false;

        var tabs = Resolver().ResolveTabs(engagement, Roles.Client);

        Assert.Equal("todo", tabs["docs"].State);
        Assert.Equal("lock", tabs["plan"].State);
        Assert.Contains("firma", tabs["plan"].Reason);
    }

    [Fact]
    public void Scope_AvailableBeforePlanning()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;
        engagement.CredentialsDone = false;

        var client = Resolver().ResolveTabs(engagement, Roles.Client);
        var pmo = Resolver().ResolveTabs(engagement, Roles.PMO);
        var pentester = Resolver().ResolveTabs(engagement, Roles.Pentester);

        Assert.Equal("todo", client["scope"].State);
        Assert.Contains("scope", client["scope"].Reason);
        Assert.Equal("todo", pmo["scope"].State);

        // Anche il Pentester può compilare se serve (il Manager pure).
        Assert.Equal("todo", pentester["scope"].State);
    }

    [Fact]
    public void Scope_AndCredentials_LockedUntilDocsAreComplete()
    {
        var engagement = NewEngagement();
        engagement.OrderConfirmed = false;

        var tabs = Resolver().ResolveTabs(engagement, Roles.Client);

        Assert.Equal("lock", tabs["scope"].State);
        Assert.Equal("lock", tabs["cred"].State);
        Assert.Contains("firma", tabs["scope"].Reason);
        Assert.False(EngagementWorkflow.CanEditPreparation(
            engagement,
            Roles.Client,
            EngagementPhase.Docs));
    }

    [Fact]
    public void Credentials_VisibleBeforePlanningToAllowedRoles()
    {
        var engagement = NewEngagement();
        engagement.CredentialsDone = false;
        engagement.ScopeDone = false;

        var pmo = Resolver().ResolveTabs(engagement, Roles.PMO);
        var client = Resolver().ResolveTabs(engagement, Roles.Client);
        var manager = Resolver().ResolveTabs(engagement, Roles.Manager);
        var pentester = Resolver().ResolveTabs(engagement, Roles.Pentester);

        Assert.Equal("todo", pmo["cred"].State);
        Assert.Equal("todo", client["cred"].State);
        // Il Manager vede e compila le credenziali.
        Assert.Equal("todo", manager["cred"].State);
        Assert.Equal("wait", pentester["cred"].State);
    }

    [Fact]
    public void Preparation_EditableFromScopeOnwards()
    {
        var engagement = NewEngagement();

        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Client, EngagementPhase.Scope));
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Client, EngagementPhase.Planning));
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Client, EngagementPhase.Credentials));
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Client, EngagementPhase.Scheduled));
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.PMO, EngagementPhase.Running));
        // Il Manager può sempre sostituirsi; il Pentester fino all'avvio.
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Manager, EngagementPhase.Scheduled));
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Pentester, EngagementPhase.Planning));
        Assert.False(EngagementWorkflow.CanEditPreparation(
            engagement, Roles.Pentester, EngagementPhase.Running));
    }

    [Fact]
    public void Tabs_ManagerCanEditCredentials()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var manager = Resolver().ResolveTabs(engagement, Roles.Manager);
        var pentester = Resolver().ResolveTabs(engagement, Roles.Pentester);
        var auditor = Resolver().ResolveTabs(engagement, Roles.Auditor);

        Assert.Equal("ok", manager["cred"].State);
        Assert.Equal("ok", pentester["cred"].State);
        Assert.Equal("lock", auditor["cred"].State);
        Assert.True(EngagementWorkflow.CanEditCredentials(
            engagement,
            Roles.Manager,
            EngagementPhase.Scheduled));
        // Il Manager può sostituirsi anche nella compilazione dello scope.
        Assert.True(EngagementWorkflow.CanEditPreparation(
            engagement,
            Roles.Manager,
            EngagementPhase.Scheduled));
    }

    [Fact]
    public void Tabs_ClientWaitsForUnpublishedReports()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        AddReport(engagement, published: false);
        engagement.ReviewStatus = ReviewStatus.Pending;

        var client = Resolver().ResolveTabs(engagement, Roles.Client);
        var pentester = Resolver().ResolveTabs(engagement, Roles.Pentester);

        Assert.Equal("wait", client["reports"].State);
        Assert.Contains("pubblicazione", client["reports"].Reason);

        Assert.Equal("wait", pentester["reports"].State);
        Assert.Contains("revisione", pentester["reports"].Reason);
    }

    [Fact]
    public void WaitingState_ReportsHowLong()
    {
        var engagement = NewEngagement();
        AddPendingProposal(engagement, ProposalParty.Provider);
        engagement.Proposals[0] = new Proposal
        {
            Id = "P-002",
            From = ProposalParty.Provider,
            By = "Provider",
            StartDate = Today.AddDays(2),
            EndDate = Today.AddDays(5),
            Date = Today.AddDays(-6),
            Status = ProposalStatus.Pending
        };

        var state = Resolver().ResolveAction(engagement, Roles.PMO);

        Assert.Equal("waiting", state.Kind);
        Assert.Equal(Today.AddDays(-6), state.Since);
    }
}
