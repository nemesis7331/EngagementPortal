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
        TenantId = "tenant-client",
        ClientName = "Nova Bank",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Provider Cyberdefence",
        HasManleva = true,
        OrderConfirmed = true,
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
        engagement.CredentialsDone = false;
        AddAcceptedProposal(engagement, Today.AddDays(2), Today.AddDays(5));

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Client);

        Assert.NotNull(todo);
        Assert.Equal("Compila lo scope", todo.Action);
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
        Assert.Equal("Inserisci le credenziali", todo.Action);
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
    public void Todo_Manager_PendingReview_EvenWhenRunning()
    {
        var engagement = NewEngagement();
        AddAcceptedProposal(engagement, Today.AddDays(-1), Today.AddDays(3));
        engagement.ReviewStatus = ReviewStatus.Pending;

        var todo = new TodoResolver(Workflow())
            .Resolve(engagement, Roles.Manager);

        Assert.NotNull(todo);
        Assert.Equal("Rivedi i deliverables", todo.Action);
    }

    [Fact]
    public void StepIndex_ReportingIsFour_DoneIsFive()
    {
        Assert.Equal(4, EngagementWorkflow.StepIndex(EngagementPhase.Reporting));
        Assert.Equal(5, EngagementWorkflow.StepIndex(EngagementPhase.Done));
    }
}
