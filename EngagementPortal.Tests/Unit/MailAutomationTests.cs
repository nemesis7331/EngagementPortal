using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngagementPortal.Tests.Unit;

public sealed class MailAutomationTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private sealed class FixedDateProvider(DateOnly today) : IDateProvider
    {
        public DateOnly Today => today;

        public DateTimeOffset UtcNow =>
            new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    [Fact]
    public void ProcessDue_CreatesStartEmailOnce()
    {
        var engagements = new InMemoryEngagementRepository();
        var clients = new InMemoryClientRepository();
        var settings = new InMemoryAppSettingsRepository();

        var engagement = new Engagement
        {
            Id = "ENG-AUTO-001",
            ClientId = "contoso",
            ClientName = "Contoso",
            Name = "Automazione email",
            Type = EngagementType.PenetrationTest,
            Owner = "Sara Conti",
            HasManleva = true,
            OrderConfirmed = true,
            Cost = 12000m,
            Margin = 4800m,
            FinancialsApproved = true,
            ScopeDone = true,
            CredentialsDone = true
        };

        engagement.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Provider",
            StartDate = Today.AddDays(-1),
            EndDate = Today.AddDays(3),
            Status = ProposalStatus.Accepted
        });

        engagements.Save(engagement);

        var services = new ServiceCollection();
        services.AddSingleton<IEngagementRepository>(engagements);
        services.AddSingleton<IClientRepository>(clients);
        services.AddSingleton<IAppSettingsRepository>(settings);

        using var provider = services.BuildServiceProvider();
        var clock = new FixedDateProvider(Today);

        var service = new MailAutomationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new EngagementWorkflow(clock),
            NullLogger<MailAutomationService>.Instance);

        service.ProcessDue();
        service.ProcessDue();

        var stored = engagements.Get("ENG-AUTO-001", "contoso", false);
        Assert.NotNull(stored);
        Assert.Single(stored.MailLog, entry => entry.Type == "start");
        Assert.Contains(
            stored.MailLog,
            entry => entry.Type == "start" &&
                entry.Auto &&
                entry.Subject == "Avvio attività – Automazione email");
        Assert.DoesNotContain(stored.MailLog, entry => entry.Type == "end");
    }

    [Fact]
    public void ProcessDue_DoesNotSendStartBeforeDocsComplete()
    {
        var engagements = new InMemoryEngagementRepository();
        var clients = new InMemoryClientRepository();
        var settings = new InMemoryAppSettingsRepository();

        var engagement = new Engagement
        {
            Id = "ENG-AUTO-DOCS",
            ClientId = "contoso",
            ClientName = "Contoso",
            Name = "Senza costi",
            Type = EngagementType.PenetrationTest,
            Owner = "Sara Conti",
            HasManleva = true,
            OrderConfirmed = true,
            ScopeDone = true,
            CredentialsDone = true
        };

        engagement.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Provider",
            StartDate = Today.AddDays(-1),
            EndDate = Today.AddDays(3),
            Status = ProposalStatus.Accepted
        });

        engagements.Save(engagement);

        var services = new ServiceCollection();
        services.AddSingleton<IEngagementRepository>(engagements);
        services.AddSingleton<IClientRepository>(clients);
        services.AddSingleton<IAppSettingsRepository>(settings);

        using var provider = services.BuildServiceProvider();
        var clock = new FixedDateProvider(Today);

        var service = new MailAutomationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new EngagementWorkflow(clock),
            NullLogger<MailAutomationService>.Instance);

        service.ProcessDue();

        var stored = engagements.Get("ENG-AUTO-DOCS", "contoso", false);
        Assert.NotNull(stored);
        Assert.DoesNotContain(stored.MailLog, entry => entry.Type == "start");
    }
}
