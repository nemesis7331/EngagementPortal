using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IClientRepository
{
    IReadOnlyList<ClientAccount> List();
    ClientAccount? Get(string clientId);
    ClientAccount Add(string name, string? website);
    ClientAccount? Update(string clientId, string name, string? website);
    ClientUser? AddUser(string clientId, string name, string email);
    bool RemoveUser(string clientId, string userId);
    bool Exists(string clientId);
}

public sealed class InMemoryClientRepository : IClientRepository
{
    private readonly object gate = new();
    private readonly List<ClientAccount> clients = CreateSeed();

    public IReadOnlyList<ClientAccount> List()
    {
        lock (gate)
        {
            return clients.ToList();
        }
    }

    public ClientAccount? Get(string clientId)
    {
        lock (gate)
        {
            return clients.FirstOrDefault(client => client.Id == clientId);
        }
    }

    public bool Exists(string clientId)
    {
        lock (gate)
        {
            return clients.Any(client => client.Id == clientId);
        }
    }

    public ClientAccount Add(string name, string? website)
    {
        lock (gate)
        {
            var account = new ClientAccount
            {
                Id = UniqueId(Slug(name)),
                Name = name.Trim(),
                Website = website
            };

            clients.Add(account);
            return account;
        }
    }

    public ClientAccount? Update(
        string clientId,
        string name,
        string? website)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.Id == clientId);

            if (account is null)
                return null;

            account.Name = name.Trim();
            account.Website = website;
            return account;
        }
    }

    public ClientUser? AddUser(
        string clientId,
        string name,
        string email)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.Id == clientId);

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

    public bool RemoveUser(string clientId, string userId)
    {
        lock (gate)
        {
            var account = clients.FirstOrDefault(client =>
                client.Id == clientId);

            var user = account?.Users
                .FirstOrDefault(item => item.Id == userId);

            if (account is null || user is null)
                return false;

            account.Users.Remove(user);
            return true;
        }
    }

    private string UniqueId(string slug)
    {
        var candidate = slug;
        var index = 2;

        while (clients.Any(client => client.Id == candidate))
        {
            candidate = $"{slug}-{index}";
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

    public static List<ClientAccount> CreateSeed() =>
    [
        NewClient(
            "contoso",
            "Contoso",
            "https://contoso.com",
            ("Marta Rinaldi", "marta.rinaldi@contoso.example"),
            ("Anna Rossi", "anna.rossi@contoso.example")),
        NewClient(
            "fabrikam",
            "Fabrikam",
            "https://fabrikam.com",
            ("Paolo Bassi", "paolo.bassi@fabrikam.example")),
        NewClient(
            "novabank",
            "Nova Bank",
            null,
            ("Giulia Sala", "giulia.sala@novabank.example")),
        NewClient(
            "orion",
            "Orion Energia",
            "https://orionenergia.com",
            ("Andrea Conti", "andrea.conti@orion.example"))
    ];

    private static ClientAccount NewClient(
        string clientId,
        string name,
        string? website,
        params (string Name, string Email)[] users)
    {
        var account = new ClientAccount
        {
            Id = clientId,
            Name = name,
            Website = website
        };

        for (var index = 0; index < users.Length; index++)
        {
            account.Users.Add(new ClientUser(
                $"CU-{clientId}-{index + 1}",
                users[index].Name,
                users[index].Email));
        }

        return account;
    }
}
