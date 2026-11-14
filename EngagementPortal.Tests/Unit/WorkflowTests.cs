using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public class WorkflowTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private sealed class FixedDateProvider : IDateProvider
    {
        public FixedDateProvider(DateOnly today)
        {
            Today = today;
        }

        public DateOnly Today { get; }

        public DateTimeOffset UtcNow =>
            new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static EngagementWorkflow Workflow() =>
        new(new FixedDateProvider(Today));

    private static Engagement NewEngagement() => new()
    {
        Id = "ENG-2026-001",
        ClientId = "client",
        ClientName = "Nova Bank",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Provider Pentester",
        HasManleva = true,
        OrderConfirmed = true,
        Cost = 12000m,
        Margin = 46m,
        FinancialsApproved = true,
        ScopeDone = true,
        CredentialsDone = true
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
            Status = ProposalStatus.Accepted
        });
    }

    [Fact]
    public void MissingManleva_ReturnsDocs()
    {
        var engagement = NewEngagement();
        engagement.HasManleva = false;
        AddAcceptedProposal(engagement, Today, Today.AddDays(2));

        Assert.Equal(EngagementPhase.Docs, Workflow().Resolve(engagement));
    }

    [Fact]
    public void MissingOrder_ReturnsDocs()
    {
        var engagement = NewEngagement();
        engagement.OrderConfirmed = false;
        AddAcceptedProposal(engagement, Today, Today.AddDays(2));

        Assert.Equal(EngagementPhase.Docs, Workflow().Resolve(engagement));
    }

    [Fact]
    public void MissingFinancials_ReturnsDocs()
    {
        var engagement = NewEngagement();
        engagement.Cost = null;
        engagement.Margin = null;
        AddAcceptedProposal(engagement, Today, Today.AddDays(2));

        Assert.Equal(EngagementPhase.Docs, Workflow().Resolve(engagement));
    }

    [Fact]
    public void PendingFinancialApproval_ReturnsDocs()
    {
        var engagement = NewEngagement();
        engagement.FinancialsApproved = false;
        AddAcceptedProposal(engagement, Today, Today.AddDays(2));

        Assert.Equal(EngagementPhase.Docs, Workflow().Resolve(engagement));
    }

    [Fact]
    public void FinancialApproval_IsManagerOnly()
    {
        var engagement = NewEngagement();
        engagement.FinancialsApproved = false;

        Assert.True(EngagementWorkflow.CanApproveFinancials(
            engagement, Roles.Manager, EngagementPhase.Docs));
        Assert.False(EngagementWorkflow.CanApproveFinancials(
            engagement, Roles.PMO, EngagementPhase.Docs));
        Assert.False(EngagementWorkflow.CanApproveFinancials(
            engagement, Roles.Manager, EngagementPhase.Scope));
    }

    [Fact]
    public void Financials_EditableOnlyInFirstPhaseByPmoOrManager()
    {
        var engagement = NewEngagement();

        Assert.True(EngagementWorkflow.CanEditFinancials(
            engagement, Roles.PMO, EngagementPhase.Docs));
        Assert.True(EngagementWorkflow.CanEditFinancials(
            engagement, Roles.Manager, EngagementPhase.Docs));
        Assert.False(EngagementWorkflow.CanEditFinancials(
            engagement, Roles.Pentester, EngagementPhase.Docs));
        Assert.False(EngagementWorkflow.CanEditFinancials(
            engagement, Roles.PMO, EngagementPhase.Scope));
    }

    [Fact]
    public void NoAcceptedProposal_ReturnsPlanning()
    {
        Assert.Equal(
            EngagementPhase.Planning,
            Workflow().Resolve(NewEngagement()));
    }

    [Fact]
    public void PublishedReport_ReturnsDone_EvenBeforeStart()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(10), Today.AddDays(20));
        engagement.Reports.Add(new ReportFile(
            "R-001",
            "report.pdf",
            "pdf",
            1024,
            DateTimeOffset.UtcNow,
            true));

        Assert.Equal(EngagementPhase.Done, Workflow().Resolve(engagement));
    }

    [Fact]
    public void PastEndWithMissingPreparation_ReturnsReporting()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(-10), Today.AddDays(-1));

        Assert.Equal(
            EngagementPhase.Reporting,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void BeforeStart_ReturnsScheduled()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        Assert.Equal(
            EngagementPhase.Scheduled,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void InsideWindow_ReturnsRunning()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-1), Today.AddDays(3));

        Assert.Equal(
            EngagementPhase.Running,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void AfterEndWithoutPublishedReport_ReturnsReporting()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        engagement.Reports.Add(new ReportFile(
            "R-001",
            "draft.pdf",
            "pdf",
            1024,
            DateTimeOffset.UtcNow,
            false));

        Assert.Equal(
            EngagementPhase.Reporting,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void Todo_ScopeBeforeCredentials()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;
        engagement.CredentialsDone = true;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Client);

        Assert.NotNull(todo);
        Assert.Equal("Compila lo scope dell'attività", todo.Action);
        Assert.Equal("scope", todo.TargetTab);
    }

    [Fact]
    public void Todo_BothPreparationItemsMissing_ScopeFirst()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;
        engagement.CredentialsDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Client);

        Assert.NotNull(todo);
        Assert.Equal("Compila lo scope dell'attività", todo.Action);
        Assert.Equal("scope", todo.TargetTab);
    }

    [Fact]
    public void Todo_CredentialsWhenScopeDone()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = true;
        engagement.CredentialsDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Client);

        Assert.NotNull(todo);
        Assert.Equal("Fornisci le credenziali di accesso", todo.Action);
        Assert.Equal("cred", todo.TargetTab);
    }

    [Fact]
    public void Todo_Auditor_ReturnsNull()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today, Today.AddDays(2));

        Assert.Null(
            new TodoResolver(Workflow())
                .Resolve(engagement, Roles.Auditor));
    }

    [Fact]
    public void Todo_Manager_ApprovesPendingProposal()
    {
        var engagement = NewEngagement();
        engagement.Proposals.Add(new Proposal
        {
            Id = "P-002",
            From = ProposalParty.Client,
            By = "Cliente",
            StartDate = Today.AddDays(2),
            EndDate = Today.AddDays(5),
            Status = ProposalStatus.Pending
        });

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Manager);

        Assert.NotNull(todo);
        Assert.Equal(
            "Proposta di pianificazione: approva o rifiuta.",
            todo.Action);
        Assert.Equal("plan", todo.TargetTab);

        Assert.Null(
            new TodoResolver(Workflow()).Resolve(engagement, Roles.PMO));
        Assert.Null(
            new TodoResolver(Workflow()).Resolve(engagement, Roles.Client));
    }

    [Fact]
    public void Todo_Manager_PendingReview_OnlyDuringReporting()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-1), Today.AddDays(3));
        engagement.ReviewStatus = ReviewStatus.Pending;

        Assert.Null(
            new TodoResolver(Workflow())
                .Resolve(engagement, Roles.Manager));

        var reporting = NewEngagement();
        AddAcceptedProposal(reporting, Today.AddDays(-5), Today.AddDays(-1));
        reporting.ReviewStatus = ReviewStatus.Pending;

        var todo = new TodoResolver(Workflow())
            .Resolve(reporting, Roles.Manager);

        Assert.NotNull(todo);
        Assert.Equal(
            "Rivedi i deliverables prima della pubblicazione.",
            todo.Action);
    }

    [Fact]
    public void Todo_Reporting_UsesDraftsNotPublishedReports()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-5), Today.AddDays(-1));
        engagement.Reports.Add(new ReportFile(
            "R-001",
            "draft.pdf",
            "pdf",
            1024,
            DateTimeOffset.UtcNow,
            false));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Pentester);

        Assert.NotNull(todo);
        Assert.Equal("Invia in revisione", todo.Action);
    }

    [Fact]
    public void Todo_Pmo_CompilesScopeDuringPreparation()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;
        engagement.CredentialsDone = true;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.PMO);

        Assert.NotNull(todo);
        Assert.Equal("Compila lo scope dell'attività", todo.Action);
        Assert.Equal("scope", todo.TargetTab);
    }

    [Fact]
    public void Todo_PmoAndManager_InsertFinancialsInFirstPhase()
    {
        var engagement = NewEngagement();
        engagement.Cost = null;
        engagement.Margin = null;

        var pmo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.PMO);
        var manager = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Manager);
        var client = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Client);

        Assert.NotNull(pmo);
        Assert.Equal("Inserisci costi e margine", pmo.Action);
        Assert.Equal("docs", pmo.TargetTab);

        Assert.NotNull(manager);
        Assert.Equal("Inserisci costi e margine", manager.Action);

        Assert.Null(client);
    }

    [Fact]
    public void Todo_Manager_ApprovesFinancials()
    {
        var engagement = NewEngagement();
        engagement.FinancialsApproved = false;

        var manager = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Manager);
        var pmo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.PMO);

        Assert.NotNull(manager);
        Assert.Equal("Approva costi e margine", manager.Action);
        Assert.Equal("docs", manager.TargetTab);
        Assert.Null(pmo);
    }

    [Fact]
    public void NoScope_ReturnsScope_EvenWithoutProposal()
    {
        var engagement = NewEngagement();
        engagement.ScopeDone = false;

        Assert.Equal(
            EngagementPhase.Scope,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void MissingCredentials_AfterAcceptedProposal_ReturnsCredentials()
    {
        var engagement = NewEngagement();
        engagement.CredentialsDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        Assert.Equal(
            EngagementPhase.Credentials,
            Workflow().Resolve(engagement));
    }

    [Fact]
    public void PlanningNotice_WarnsBeforeTwoWeeksFromRequest()
    {
        var engagement = new Engagement
        {
            Id = "ENG-NOTICE",
            ClientId = "client",
            ClientName = "Contoso",
            Name = "Avviso pianificazione",
            Type = EngagementType.PenetrationTest,
            Owner = "Sara Conti",
            CreatedDate = new DateTimeOffset(
                2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
        };

        var notice = EngagementWorkflow.PlanningNotice(
            engagement,
            new DateOnly(2026, 6, 5));

        Assert.NotNull(notice);
        Assert.Equal(new DateOnly(2026, 6, 1), notice.RequestDate);
        Assert.Equal(new DateOnly(2026, 6, 15), notice.AvailableFrom);
        Assert.Equal(10, notice.DaysRemaining);

        Assert.Null(EngagementWorkflow.PlanningNotice(
            engagement,
            new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void StepIndex_FollowsWorkflowOrder()
    {
        Assert.Equal(0, EngagementWorkflow.StepIndex(EngagementPhase.Docs));
        Assert.Equal(1, EngagementWorkflow.StepIndex(EngagementPhase.Scope));
        Assert.Equal(2, EngagementWorkflow.StepIndex(EngagementPhase.Planning));
        Assert.Equal(3, EngagementWorkflow.StepIndex(EngagementPhase.Credentials));
        Assert.Equal(4, EngagementWorkflow.StepIndex(EngagementPhase.Running));
        Assert.Equal(5, EngagementWorkflow.StepIndex(EngagementPhase.Reporting));
        Assert.Equal(6, EngagementWorkflow.StepIndex(EngagementPhase.Done));
    }

    [Fact]
    public void PreparationAlert_StartsTwoWeeksBeforeExecution()
    {
        var engagement = NewEngagement();
        engagement.CredentialsDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(20), Today.AddDays(30));

        Assert.Null(EngagementWorkflow.PreparationAlertFor(
            engagement,
            EngagementPhase.Credentials,
            Today));

        var near = NewEngagement();
        near.ScopeDone = false;
        AddAcceptedProposal(near, Today.AddDays(10), Today.AddDays(20));

        var alert = EngagementWorkflow.PreparationAlertFor(
            near,
            EngagementPhase.Scope,
            Today);

        Assert.NotNull(alert);
        Assert.Equal("late", alert.Level);
        Assert.Contains("scope", alert.Missing);
        Assert.Equal(10, alert.DaysToStart);
    }
}
