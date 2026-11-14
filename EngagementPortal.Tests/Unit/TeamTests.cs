using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public sealed class TeamTests
{
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
        Margin = 4800m,
        FinancialsApproved = true
    };

    private static TeamMember Member(string email, string organization) =>
        new("TM-1", "Mario Rossi", email, organization, "Referente", "Sistema",
            DateTimeOffset.UtcNow);

    [Fact]
    public void Effective_MergesBaseAndEngagementWithoutDuplicates()
    {
        var engagement = NewEngagement();
        engagement.Team.Add(Member("partner@example.com", "Partner"));

        var baseTeam = new[]
        {
            Member("cliente@example.com", "Cliente"),
            Member("PARTNER@example.com", "Cliente")
        };

        var effective = Team.Effective(engagement, baseTeam);

        Assert.Equal(2, effective.Count);
        Assert.Equal("cliente@example.com", effective[0].Email);
        Assert.Equal("PARTNER@example.com", effective[1].Email);
    }

    [Fact]
    public void Invite_AllowedToEveryoneExceptAuditor()
    {
        Assert.True(Team.CanInvite(Roles.Client));
        Assert.True(Team.CanInvite(Roles.PMO));
        Assert.True(Team.CanInvite(Roles.Pentester));
        Assert.True(Team.CanInvite(Roles.Manager));
        Assert.False(Team.CanInvite(Roles.Auditor));
    }

    [Fact]
    public void Remove_ManagerAny_ClientOnlyClientSide()
    {
        var client = Member("cliente@example.com", "Cliente");
        var partner = Member("partner@example.com", "Partner");

        Assert.True(Team.CanRemove(Roles.Manager, client));
        Assert.True(Team.CanRemove(Roles.Manager, partner));
        Assert.True(Team.CanRemove(Roles.Client, client));
        Assert.False(Team.CanRemove(Roles.Client, partner));
        Assert.False(Team.CanRemove(Roles.PMO, client));
        Assert.False(Team.CanRemove(Roles.Pentester, client));
        Assert.False(Team.CanRemove(Roles.Auditor, client));
    }
}
