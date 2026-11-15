using EngagementPortal.Web.Domain;

namespace EngagementPortal.Web.Services;

public interface IUserRepository
{
    IReadOnlyList<UserAccount> List();
    UserAccount? Get(string id);
    UserAccount Add(string name, string email, string role);
    void Save(UserAccount user);
    bool Remove(string id);
}

/// <summary>
/// Directory interna del provider in memoria. In produzione la stessa
/// interfaccia è implementata su SQL (tabella Users, payload JSON).
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly object gate = new();
    private readonly List<UserAccount> users = CreateSeed();

    public IReadOnlyList<UserAccount> List()
    {
        lock (gate)
            return users.ToList();
    }

    public UserAccount? Get(string id)
    {
        lock (gate)
            return users.FirstOrDefault(user => user.Id == id);
    }

    public UserAccount Add(string name, string email, string role)
    {
        lock (gate)
        {
            var user = new UserAccount
            {
                Id = $"USR-{Guid.NewGuid():N}"[..10],
                Name = name.Trim(),
                Email = email.Trim(),
                Role = role
            };

            users.Add(user);
            return user;
        }
    }

    public void Save(UserAccount user)
    {
        lock (gate)
        {
            var index = users.FindIndex(item => item.Id == user.Id);

            if (index >= 0)
                users[index] = user;
            else
                users.Add(user);
        }
    }

    public bool Remove(string id)
    {
        lock (gate)
        {
            var user = users.FirstOrDefault(item => item.Id == id);

            if (user is null)
                return false;

            users.Remove(user);
            return true;
        }
    }

    public static List<UserAccount> CreateSeed() =>
    [
        new UserAccount
        {
            Id = "USR-001",
            Name = "Giorgio Marino",
            Email = "giorgio.marino@provider.example",
            Role = Roles.Manager
        },
        new UserAccount
        {
            Id = "USR-002",
            Name = "Luca Ferri",
            Email = "luca.ferri@provider.example",
            Role = Roles.PMO,
            ClientIds = ["contoso", "fabrikam"]
        },
        new UserAccount
        {
            Id = "USR-003",
            Name = "Sara Conti",
            Email = "sara.conti@provider.example",
            Role = Roles.Pentester,
            ClientIds = ["contoso", "novabank"]
        },
        new UserAccount
        {
            Id = "USR-004",
            Name = "Marco Verdi",
            Email = "marco.verdi@provider.example",
            Role = Roles.Pentester,
            EngagementIds = ["ENG-2026-001", "ENG-2026-003"]
        },
        new UserAccount
        {
            Id = "USR-005",
            Name = "Elena Fabbri",
            Email = "elena.fabbri@provider.example",
            Role = Roles.Pentester,
            ClientIds = ["fabrikam"]
        },
        new UserAccount
        {
            Id = "USR-006",
            Name = "Paolo Galli",
            Email = "paolo.galli@provider.example",
            Role = Roles.Auditor,
            ClientIds = ["novabank"]
        }
    ];
}
