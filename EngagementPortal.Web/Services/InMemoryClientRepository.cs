using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IClientRepository
{
    IReadOnlyList<ClientAccount> List();
    ClientAccount? Get(string tenantId);
    ClientAccount Add(string name, string? tenantId);
    ClientAccount? Rename(string tenantId, string name);
    ClientUser? AddUser(string tenantId, string name, string email);
    bool RemoveUser(string tenantId, string userId);
    bool Exists(string tenantId);
}

public sealed class InMemoryClientRepository : IClientRepository
{
    private readonly object gate = new();
    private readonly List<ClientAccount> clients = Seed();

    public IReadOnlyList<ClientAccount> List()
    {
        lock (gate)
        {
            return clients.ToList();
        }
    }

    public ClientAccount? Get(string tenantId)
    {
        lock (gate)
        {
            return clients.FirstOrDefault(client =>
                client.TenantId == tenantId);
        }
    }

    public bool Exists(string tenantId)
    {
        lock (gate)
        {
            return clients.Any(client => client.TenantId == tenantId);
        }
    }

    public ClientAccount Add(string name, string? tenantId)
    {
        lock (gate)
        {
            var resolved = string.IsNullOrWhiteSpace(tenantId)
                ? UniqueTenantId(Slug(name))
                : tenantId.Trim();

            var account = new ClientAccount
            {
                TenantId = resolved,
                Name = name.Trim()
            };

            clients.Add(account);
            return account;
        }
    }

    public ClientAccount? Rename(string tenantId, string name)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.TenantId == tenantId);

            if (account is null)
                return null;

            account.Name = name.Trim();
            return account;
        }
    }

    public ClientUser? AddUser(
        string tenantId,
        string name,
        string email)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.TenantId == tenantId);

            if (account is null)
                return null;

            var user = new ClientUser(
                $"CU-{Guid.NewGuid():N}"[..10],
                name.Trim(),
                email.Trim());

            account.Users.Add(user);
            return user;
        }
    }

    public bool RemoveUser(string tenantId, string userId)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.TenantId == tenantId);

            var user = account?.Users
                .FirstOrDefault(item => item.Id == userId);

            if (account is null || user is null)
                return false;

            account.Users.Remove(user);
            return true;
        }
    }

    private string UniqueTenantId(string slug)
    {
        var candidate = $"tenant-{slug}";
        var index = 2;

        while (clients.Any(client => client.TenantId == candidate))
        {
            candidate = $"tenant-{slug}-{index}";
            index++;
        }

        return candidate;
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars)
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(slug)
            ? "cliente"
            : slug;
    }

    private static List<ClientAccount> Seed() =>
    [
        NewClient(
            "tenant-contoso",
            "Contoso",
            ("Marta Rinaldi", "marta.rinaldi@contoso.example"),
            ("Anna Rossi", "anna.rossi@contoso.example")),
        NewClient(
            "tenant-fabrikam",
            "Fabrikam",
            ("Paolo Bassi", "paolo.bassi@fabrikam.example")),
        NewClient(
            "tenant-novabank",
            "Nova Bank",
            ("Giulia Sala", "giulia.sala@novabank.example")),
        NewClient(
            "tenant-orion",
            "Orion Energia",
            ("Andrea Conti", "andrea.conti@orion.example"))
    ];

    private static ClientAccount NewClient(
        string tenantId,
        string name,
        params (string Name, string Email)[] users)
    {
        var account = new ClientAccount
        {
            TenantId = tenantId,
            Name = name
        };

        for (var index = 0; index < users.Length; index++)
        {
            account.Users.Add(new ClientUser(
                $"CU-{tenantId}-{index + 1}",
                users[index].Name,
                users[index].Email));
        }

        return account;
    }
}
