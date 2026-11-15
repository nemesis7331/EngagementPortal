using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

/// <summary>
/// Appartenenza alle due liste base del cliente: il legame è la persona
/// (PersonId), l'email serve solo per agganciare le voci storiche.
/// </summary>
public sealed class ClientDistributionTests
{
    [Fact]
    public void Include_CreatesTheEntryOnce_LinkedToThePerson()
    {
        var account = NewAccount();
        var list = account.DistributionList;

        ClientDistribution.Include(
            list, "USR-1", "Marta Rinaldi", "marta@anthropic.example", "Cliente");
        ClientDistribution.Include(
            list, "USR-1", "Marta Rinaldi", "marta@anthropic.example", "Cliente");

        var entry = Assert.Single(list);
        Assert.Equal("USR-1", entry.PersonId);
        Assert.Equal("marta@anthropic.example", entry.Email);
    }

    [Fact]
    public void Include_AdoptsALegacyEntryFoundByEmail()
    {
        var account = NewAccount();
        account.DistributionList.Add(new DistributionEntry(
            "DL-1", "Marta Rinaldi", "marta@anthropic.example", "Cliente"));

        ClientDistribution.Include(
            account.DistributionList,
            "USR-1",
            "Marta Rinaldi",
            "marta@anthropic.example",
            "Cliente");

        var entry = Assert.Single(account.DistributionList);
        Assert.Equal("USR-1", entry.PersonId);
    }

    [Fact]
    public void Include_KeepsExternalRecipientsWithoutAPerson()
    {
        var account = NewAccount();

        ClientDistribution.Include(
            account.DeliverablesDistributionList,
            null,
            "Amministrazione",
            "amministrazione@anthropic.example",
            "Cliente");

        var entry = Assert.Single(account.DeliverablesDistributionList);
        Assert.Null(entry.PersonId);
    }

    [Fact]
    public void Exclude_TouchesOnlyTheRequestedList()
    {
        var account = NewAccount();
        account.DistributionList.Add(new DistributionEntry(
            "DL-1", "Marta", "marta@anthropic.example", "Cliente", "USR-1"));
        account.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-2", "Marta", "marta@anthropic.example", "Cliente", "USR-1"));

        ClientDistribution.Exclude(
            account.DistributionList, "USR-1", "marta@anthropic.example");

        Assert.Empty(account.DistributionList);
        Assert.Single(account.DeliverablesDistributionList);
    }

    [Fact]
    public void Exclude_IsIdempotent()
    {
        var account = NewAccount();

        ClientDistribution.Exclude(
            account.DistributionList, "USR-1", "marta@anthropic.example");

        Assert.Empty(account.DistributionList);
    }

    [Fact]
    public void RemovePerson_LeavesBothLists()
    {
        var account = NewAccount();
        account.DistributionList.Add(new DistributionEntry(
            "DL-1", "Marta", "marta@anthropic.example", "Cliente", "USR-1"));
        account.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-2", "Marta", "marta@anthropic.example", "Cliente", "USR-1"));
        account.DeliverablesDistributionList.Add(new DistributionEntry(
            "DL-3", "Amministrazione", "amministrazione@anthropic.example", "Cliente"));

        ClientDistribution.RemovePerson(
            account, "USR-1", "marta@anthropic.example");

        Assert.Empty(account.DistributionList);
        var remaining = Assert.Single(account.DeliverablesDistributionList);
        Assert.Equal("amministrazione@anthropic.example", remaining.Email);
    }

    [Fact]
    public void FindPerson_ReadsPortalUsersAndTeamMembers()
    {
        var account = NewAccount();
        account.Users.Add(new ClientUser(
            "USR-1", "Marta Rinaldi", "marta@anthropic.example"));
        account.Team.Add(new TeamMember(
            "TM-1",
            "Sara Conti",
            "sara@provider.example",
            "Partner",
            "Pentester",
            "Sistema",
            DateTimeOffset.UnixEpoch));

        var user = ClientDistribution.FindPerson(account, "USR-1");
        var member = ClientDistribution.FindPerson(account, "TM-1");

        Assert.Equal("Marta Rinaldi", user?.Name);
        Assert.Equal("Cliente", user?.Organization);
        Assert.Equal("Sara Conti", member?.Name);
        Assert.Equal("Partner", member?.Organization);
        Assert.Null(ClientDistribution.FindPerson(account, "USR-X"));
    }

    [Fact]
    public void Find_MatchesLegacyEntriesByEmailIgnoringCase()
    {
        var account = NewAccount();
        account.DistributionList.Add(new DistributionEntry(
            "DL-1", "Marta", "Marta@Anthropic.Example", "Cliente"));

        var found = ClientDistribution.Find(
            account.DistributionList, "USR-1", "marta@anthropic.example");

        Assert.NotNull(found);
        Assert.Equal("DL-1", found.Id);
    }

    private static ClientAccount NewAccount() => new()
    {
        Id = "anthropic",
        Name = "Anthropic"
    };
}
