using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngagementPortal.Tests.Unit;

public sealed class DistributionTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private sealed class FixedDateProvider(DateOnly today) : IDateProvider
    {
        public DateOnly Today => today;

        public DateTimeOffset UtcNow =>
            new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    [Fact]
    public void DeliverablesList_IsSeparateFromNotifications()
    {
        var engagement = NewEngagement();
        engagement.DistributionList.Add(new DistributionEntry(
            "DL-N-1", "Luca Ferri", "luca@provider.example", "Provider PMO"));
        engagement.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-D-1", "Security Cliente", "security@anthropic.example", "Cliente"));

        var notifications = Distribution.Effective(
            engagement,
            [new DistributionEntry("DL-B-1", "Marta", "marta@anthropic.example", "Cliente")]);
        var deliverables = Distribution.EffectiveDeliverables(
            engagement,
            [],
            []);

        Assert.Equal(
            ["marta@anthropic.example", "luca@provider.example"],
            notifications.Select(entry => entry.Email));
        Assert.Equal(
            ["security@anthropic.example"],
            deliverables.Select(entry => entry.Email));
    }

    [Fact]
    public void DeliverablesList_MergesBaseAndEngagementWithoutDuplicates()
    {
        var engagement = NewEngagement();
        engagement.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-D-1", "Security Cliente", "security@anthropic.example", "Cliente"));
        engagement.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-D-2", "Referente tecnico", "tech@anthropic.example", "Cliente"));

        var effective = Distribution.EffectiveDeliverables(
            engagement,
            [
                new DistributionEntry(
                    "DL-D-B1", "Security", "SECURITY@anthropic.example", "Cliente"),
                new DistributionEntry(
                    "DL-D-B2", "Marta", "marta@anthropic.example", "Cliente")
            ],
            []);

        Assert.Equal(
            ["SECURITY@anthropic.example", "marta@anthropic.example", "tech@anthropic.example"],
            effective.Select(entry => entry.Email));
    }

    [Fact]
    public void DeliverablesList_FallsBackToNotificationsWhenEmpty()
    {
        var engagement = NewEngagement();
        engagement.DistributionList.Add(new DistributionEntry(
            "DL-N-1", "Luca Ferri", "luca@provider.example", "Provider PMO"));

        var notifications = Distribution.Effective(
            engagement,
            [new DistributionEntry("DL-B-1", "Marta", "marta@anthropic.example", "Cliente")]);
        var deliverables = Distribution.EffectiveDeliverables(
            engagement,
            [],
            [new DistributionEntry("DL-B-1", "Marta", "marta@anthropic.example", "Cliente")]);

        Assert.Equal(
            notifications.Select(entry => entry.Email),
            deliverables.Select(entry => entry.Email));
        Assert.True(Distribution.UsesNotificationFallback(engagement, []));
    }

    [Fact]
    public void ProcessDue_SendsDeliverablesToDeliverablesList()
    {
        var engagements = new InMemoryEngagementRepository();
        var clients = new InMemoryClientRepository();
        var settings = new InMemoryAppSettingsRepository();

        var engagement = NewEngagement();
        engagement.Mail.Start = false;
        engagement.Mail.End = false;
        engagement.DistributionList.Add(new DistributionEntry(
            "DL-N-1", "Notifiche", "notifications@anthropic.example", "Cliente"));
        engagement.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-D-1", "Deliverables", "deliverables@anthropic.example", "Cliente"));
        engagement.Reports.Add(new ReportFile(
            "R-1", "Report.txt", "TXT", 100, DateTimeOffset.UtcNow, true));

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

        var stored = engagements.Get(engagement.Id, "anthropic", false);
        Assert.NotNull(stored);

        var deliver = Assert.Single(
            stored.MailLog,
            entry => entry.Type == "deliver");

        Assert.Contains("deliverables@anthropic.example", deliver.Recipients);
        Assert.DoesNotContain("notifications@anthropic.example", deliver.Recipients);
        Assert.DoesNotContain(
            "luca.ferri@provider.example",
            deliver.Recipients);
    }

    private static Engagement NewEngagement() => new()
    {
        Id = "ENG-DL-001",
        ClientId = "anthropic",
        ClientName = "Anthropic",
        Name = "Distribution list",
        Type = EngagementType.PenetrationTest,
        Owner = "Sara Conti",
        HasManleva = true,
        OrderConfirmed = true,
        Cost = 12000m,
        Margin = 46m,
        FinancialsApproved = true,
        ScopeDone = true,
        CredentialsDone = true,
        ReachabilityVerified = true
    };
}
