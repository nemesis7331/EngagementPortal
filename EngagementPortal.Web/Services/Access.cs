using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Regola di visibilità: il Manager vede tutto; il Cliente vede il proprio
/// cliente; PMO/Pentester/Auditor vedono i clienti e gli engagement
/// assegnati. Gli utenti creati prima delle assegnazioni (senza UserId)
/// mantengono il comportamento demo per compatibilità.
/// </summary>
public static class Access
{
    public static bool CanAccessClient(UserContext user, string clientId)
    {
        if (user.Role == Roles.Manager)
            return true;

        if (user.Role == Roles.Client)
            return user.ClientId == clientId;

        if (!user.Scoped)
            return true;

        return user.AssignedClientIds?.Contains(clientId) == true;
    }

    public static bool CanAccess(UserContext user, Engagement engagement)
    {
        if (user.Role == Roles.Manager)
            return true;

        if (user.Role == Roles.Client)
            return engagement.ClientId == user.ClientId;

        if (!user.Scoped)
            return true;

        return user.AssignedClientIds?.Contains(engagement.ClientId) == true ||
               user.AssignedEngagementIds?.Contains(engagement.Id) == true;
    }

    public static IReadOnlyList<Engagement> AccessibleEngagements(
        IEngagementRepository repository,
        UserContext user) =>
        repository
            .List(string.Empty, allClients: true)
            .Where(engagement => CanAccess(user, engagement))
            .ToList();

    public static IReadOnlyList<Engagement> VisibleEngagements(
        IEngagementRepository repository,
        UserContext user) =>
        AccessibleEngagements(repository, user)
            .Where(engagement =>
                string.IsNullOrWhiteSpace(user.ClientId) ||
                engagement.ClientId == user.ClientId)
            .ToList();

    public static IReadOnlyList<ClientAccount> VisibleClients(
        IClientRepository repository,
        UserContext user) =>
        repository
            .List()
            .Where(client => CanAccessClient(user, client.Id))
            .ToList();
}
