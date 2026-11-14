using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Team esterno del cliente e dell'engagement. I partecipanti interni del
/// provider restano i profili del portale; qui si gestiscono gli invitati.
/// </summary>
public static class Team
{
    public static IReadOnlyList<TeamMember> Effective(
        Engagement engagement,
        IEnumerable<TeamMember> baseTeam)
    {
        var result = new List<TeamMember>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in baseTeam.Concat(engagement.Team))
        {
            if (seen.Add(member.Email))
                result.Add(member);
        }

        return result;
    }

    public static bool CanInvite(string role) =>
        role != Roles.Auditor;

    public static bool CanRemove(string role, TeamMember member) =>
        role == Roles.Manager ||
        (role == Roles.Client && member.Organization == "Cliente");
}
