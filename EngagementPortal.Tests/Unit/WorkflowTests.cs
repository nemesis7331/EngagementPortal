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
        ClientName = "Acme",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Provider Pentester",
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
    public void Reachability_IsRequiredBeforeExecution()
    {
        var engagement = NewEngagement();
        engagement.ReachabilityVerified = false;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(6));

        // Con le credenziali ma senza verifica dei target si resta fermi qui.
        Assert.Equal(
            EngagementPhase.Reachability,
            Workflow().Resolve(engagement));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Pentester);

        Assert.NotNull(todo);
        Assert.Equal("reach", todo!.TargetTab);

        engagement.ReachabilityVerified = true;

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
    public void Financials_AreVisibleOnlyToPmoAndManager()
    {
        Assert.True(EngagementWorkflow.CanViewFinancials(Roles.PMO));
        Assert.True(EngagementWorkflow.CanViewFinancials(Roles.Manager));
        Assert.False(EngagementWorkflow.CanViewFinancials(Roles.Client));
        Assert.False(EngagementWorkflow.CanViewFinancials(Roles.Pentester));
        Assert.False(EngagementWorkflow.CanViewFinancials(Roles.Auditor));
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
            ClientName = "Anthropic",
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
        Assert.Equal(0, EngagementWorkflow.StepIndex(EngagementPhase.Setup));
        Assert.Equal(1, EngagementWorkflow.StepIndex(EngagementPhase.Docs));
        Assert.Equal(2, EngagementWorkflow.StepIndex(EngagementPhase.Scope));
        Assert.Equal(3, EngagementWorkflow.StepIndex(EngagementPhase.Planning));
        Assert.Equal(4, EngagementWorkflow.StepIndex(EngagementPhase.Credentials));
        Assert.Equal(5, EngagementWorkflow.StepIndex(EngagementPhase.Reachability));
        Assert.Equal(6, EngagementWorkflow.StepIndex(EngagementPhase.Running));
        Assert.Equal(7, EngagementWorkflow.StepIndex(EngagementPhase.Reporting));
        Assert.Equal(8, EngagementWorkflow.StepIndex(EngagementPhase.Retest));
        Assert.Equal(9, EngagementWorkflow.StepIndex(EngagementPhase.RetestReporting));
        Assert.Equal(10, EngagementWorkflow.StepIndex(EngagementPhase.Done));
    }

    [Fact]
    public void SetupIncomplete_ReturnsSetupAndLocksNextSteps()
    {
        var engagement = NewEngagement();
        engagement.SetupDone = false;
        var workflow = Workflow();
        var todos = new TodoResolver(workflow);

        Assert.Equal(EngagementPhase.Setup, workflow.Resolve(engagement));
        Assert.Equal("setup",
            todos.Resolve(engagement, Roles.PMO)?.TargetTab);

        var tabs = todos.ResolveTabs(engagement, Roles.PMO);
        Assert.Equal("todo", tabs["setup"].State);
        Assert.Equal("lock", tabs["docs"].State);
        Assert.Equal("lock", tabs["plan"].State);
    }

    [Fact]
    public void MissingReportKinds_BlockReportingCompletion()
    {
        var engagement = NewEngagement();
        engagement.DeliverableTypes.Clear();
        engagement.DeliverableTypes.AddRange(
        [
            DeliverableKinds.Executive,
            DeliverableKinds.Technical
        ]);
        engagement.Reports.Add(new ReportFile(
            "R-1",
            "technical.pdf",
            "PDF",
            10,
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            true,
            DeliverableKinds.Technical));

        Assert.False(engagement.ReportsComplete);
        Assert.Contains(DeliverableKinds.Executive,
            engagement.MissingReportTypes);

        engagement.Reports.Add(new ReportFile(
            "R-2",
            "executive.pdf",
            "PDF",
            10,
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            true,
            DeliverableKinds.Executive));

        Assert.True(engagement.ReportsComplete);
        Assert.Empty(engagement.MissingReportTypes);
    }

    [Fact]
    public void Retest_NotPlanned_ClosesWithThePublishedReport()
    {
        var engagement = PublishedEngagement();
        engagement.RetestPlanned = false;

        Assert.Equal(EngagementPhase.Done, Workflow().Resolve(engagement));
    }

    [Fact]
    public void Retest_Planned_WaitsForTheClientRequest()
    {
        var engagement = PublishedEngagement();
        engagement.RetestPlanned = true;
        var workflow = Workflow();
        var todos = new TodoResolver(workflow);

        Assert.Equal(EngagementPhase.Retest, workflow.Resolve(engagement));
        Assert.Equal(
            "Richiedi il retest a fix completati",
            todos.Resolve(engagement, Roles.Client)?.Action);
        // Solo il cliente può chiedere il retest.
        Assert.Null(todos.Resolve(engagement, Roles.PMO));
        Assert.Equal(
            "In attesa della richiesta di retest dal cliente",
            todos.ResolveAction(engagement, Roles.Manager).Label);
    }

    [Fact]
    public void RetestProposal_WaitsForTheManagerDecision()
    {
        var engagement = PublishedEngagement();
        engagement.RetestPlanned = true;
        engagement.RetestRequestedDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        engagement.RetestProposals.Add(new Proposal
        {
            Id = "RE-01",
            By = "PMO",
            StartDate = Today.AddDays(5),
            EndDate = Today.AddDays(7),
            Date = Today,
            Status = ProposalStatus.Pending
        });

        var workflow = Workflow();
        var todos = new TodoResolver(workflow);

        Assert.Equal(EngagementPhase.Retest, workflow.Resolve(engagement));
        Assert.Equal(
            "Date del retest: approva o rifiuta.",
            todos.Resolve(engagement, Roles.Manager)?.Action);
    }

    [Fact]
    public void RetestReport_SentWithoutReview_ClosesTheEngagement()
    {
        var engagement = PublishedEngagement();
        engagement.RetestPlanned = true;
        engagement.RetestRequestedDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        engagement.AcceptedRetestProposal = new Proposal
        {
            Id = "RE-01",
            By = "PMO",
            StartDate = Today.AddDays(-2),
            EndDate = Today,
            Status = ProposalStatus.Accepted
        };

        var workflow = Workflow();
        var todos = new TodoResolver(workflow);

        // Con le date approvate il Pentester conclude il retest.
        Assert.Equal(EngagementPhase.Retest, workflow.Resolve(engagement));
        Assert.Equal(
            "Concludi il retest e carica il report",
            todos.Resolve(engagement, Roles.Pentester)?.Action);

        engagement.RetestCompletedDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        engagement.RetestReport = new ReportFile(
            "RF-1",
            "retest.pdf",
            "PDF",
            10,
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            false,
            "Retest");

        Assert.Equal(
            EngagementPhase.RetestReporting,
            workflow.Resolve(engagement));

        // L'invio al cliente non passa dalla revisione del Manager.
        Assert.Equal(
            "Invia il report di retest al cliente (senza revisione)",
            todos.Resolve(engagement, Roles.Pentester)?.Action);
        Assert.Null(todos.Resolve(engagement, Roles.Manager));

        engagement.RetestReportSentDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        Assert.Equal(EngagementPhase.Done, workflow.Resolve(engagement));
    }

    [Fact]
    public void RetestClosedWithoutExecution_ClosesTheEngagement()
    {
        var engagement = PublishedEngagement();
        engagement.RetestPlanned = true;
        engagement.RetestRequestedDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var workflow = Workflow();
        var todos = new TodoResolver(workflow);

        // In attesa della richiesta di date: l'attività non si chiude da sola.
        Assert.Equal(EngagementPhase.Retest, workflow.Resolve(engagement));

        // Se il retest non si fa, PMO o Manager lo chiudono senza esecuzione.
        engagement.RetestClosedDate =
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        engagement.RetestClosedByName = "Luca Ferri";

        Assert.Equal(EngagementPhase.Done, workflow.Resolve(engagement));
        Assert.Null(todos.Resolve(engagement, Roles.PMO));
        Assert.Null(todos.Resolve(engagement, Roles.Client));
    }

    private static Engagement PublishedEngagement()
    {
        var engagement = NewEngagement();
        engagement.DeliverableTypes.Clear();
        engagement.DeliverableTypes.Add(DeliverableKinds.Technical);
        engagement.Reports.Add(new ReportFile(
            "R-1",
            "technical.pdf",
            "PDF",
            10,
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            true,
            DeliverableKinds.Technical));
        return engagement;
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
