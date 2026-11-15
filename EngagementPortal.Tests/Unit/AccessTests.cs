using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public sealed class AccessTests
{
    private static Engagement NewEngagement(string id = "ENG-1") => new()
    {
        Id = id,
        ClientId = "contoso",
        ClientName = "Contoso",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Sara Conti"
    };

    [Fact]
    public void Manager_SeesEveryClientAndEngagement()
    {
        var user = new UserContext(
            "Giorgio Marino",
            null,
            Roles.Manager,
            "USR-001");

        Assert.True(Access.CanAccess(user, NewEngagement()));
        Assert.True(Access.CanAccessClient(user, "novabank"));
    }

    [Fact]
    public void AssignedClient_SeesAllItsEngagements()
    {
        var user = new UserContext(
            "Luca Ferri",
            null,
            Roles.PMO,
            "USR-002",
            ["contoso", "fabrikam"],
            [],
            Scoped: true);

        Assert.True(Access.CanAccess(user, NewEngagement()));
        Assert.True(Access.CanAccessClient(user, "fabrikam"));
        Assert.False(Access.CanAccessClient(user, "novabank"));
    }

    [Fact]
    public void EngagementAssignment_DoesNotExposeOtherEngagements()
    {
        var user = new UserContext(
            "Marco Verdi",
            null,
            Roles.Pentester,
            "USR-004",
            [],
            ["ENG-1"],
            Scoped: true);

        Assert.True(Access.CanAccess(user, NewEngagement("ENG-1")));
        Assert.False(Access.CanAccess(user, NewEngagement("ENG-2")));
        Assert.False(Access.CanAccessClient(user, "contoso"));
    }

    [Fact]
    public void ScopedUserWithoutAssignments_SeesNothing()
    {
        var user = new UserContext(
            "Elena Fabbri",
            null,
            Roles.Pentester,
            "USR-005",
            [],
            [],
            Scoped: true);

        Assert.False(Access.CanAccess(user, NewEngagement()));
    }

    [Fact]
    public void Client_SeesOnlyOwnClient()
    {
        var user = new UserContext(
            "Marta Rinaldi",
            "contoso",
            Roles.Client,
            "CU-contoso-1");

        Assert.True(Access.CanAccess(user, NewEngagement()));
        Assert.True(Access.CanAccessClient(user, "contoso"));
        Assert.False(Access.CanAccessClient(user, "fabrikam"));
    }

    [Fact]
    public void LegacyRoleOnlyUser_KeepsDemoVisibility()
    {
        var user = new UserContext("Demo User", null, Roles.PMO);

        Assert.True(Access.CanAccess(user, NewEngagement()));
        Assert.True(Access.CanAccessClient(user, "novabank"));
    }
}
