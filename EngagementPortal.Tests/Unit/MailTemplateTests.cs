using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public sealed class MailTemplateTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static Engagement NewEngagement() => new()
    {
        Id = "ENG-2026-001",
        ClientId = "contoso",
        ClientName = "Contoso",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Sara Conti",
        HasManleva = true,
        OrderConfirmed = true,
        Cost = 12000m,
        Margin = 46m,
        FinancialsApproved = true
    };

    private static void Accept(Engagement engagement)
    {
        engagement.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Provider",
            StartDate = Today,
            EndDate = Today.AddDays(5),
            Status = ProposalStatus.Accepted
        });
    }

    [Fact]
    public void Resolve_PrefersEngagementThenClientThenStandard()
    {
        var engagement = NewEngagement();
        Accept(engagement);

        var standard = MailTemplates.Defaults();
        var client = new MailTemplateSet
        {
            Start = new MailTemplate
            {
                Subject = "Cliente {{client}}",
                Body = "Cliente"
            }
        };

        var fromClient = MailTemplates.Resolve(
            "start", engagement, standard, client, Today);
        Assert.Equal("client", fromClient.Source);
        Assert.Equal("Cliente Contoso", fromClient.Subject);

        engagement.MailTemplates.Start = new MailTemplate
        {
            Subject = "Engagement {{code}}",
            Body = "Engagement"
        };

        var fromEngagement = MailTemplates.Resolve(
            "start", engagement, standard, client, Today);
        Assert.Equal("engagement", fromEngagement.Source);
        Assert.Equal("Engagement ENG-2026-001", fromEngagement.Subject);
    }

    [Fact]
    public void Resolve_DefaultRendersDatesAndClient()
    {
        var engagement = NewEngagement();
        Accept(engagement);

        var resolved = MailTemplates.Resolve(
            "start",
            engagement,
            MailTemplates.Defaults(),
            null,
            Today);

        Assert.Equal("standard", resolved.Source);
        Assert.Contains("External Penetration Test", resolved.Subject);
        Assert.Contains("15/06/2026", resolved.Body);
        Assert.Contains("20/06/2026", resolved.Body);
        Assert.DoesNotContain("{{", resolved.Subject);
        Assert.DoesNotContain("{{", resolved.Body);
    }

    [Fact]
    public void EffectiveDistribution_MergesBaseAndEngagementWithoutDuplicates()
    {
        var engagement = NewEngagement();
        engagement.DistributionList.Add(new DistributionEntry(
            "DL-1", "Luca Ferri", "luca@provider.example", "Provider PMO"));

        var baseList = new[]
        {
            new DistributionEntry("DL-B-1", "Marta", "marta@contoso.example", "Cliente"),
            new DistributionEntry("DL-B-2", "Luca", "LUCA@provider.example", "Cliente")
        };

        var effective = Distribution.Effective(engagement, baseList);

        Assert.Equal(2, effective.Count);
        Assert.Equal("marta@contoso.example", effective[0].Email);
        Assert.Equal("LUCA@provider.example", effective[1].Email);
    }
}
