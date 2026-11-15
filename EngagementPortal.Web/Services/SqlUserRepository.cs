using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Implementazione SQL di <see cref="IUserRepository"/>: una riga per utente
/// con payload JSON, come per clienti ed engagement.
/// </summary>
public sealed class SqlUserRepository(
    PortalDbContext db,
    IDateProvider clock) : IUserRepository
{
    public IReadOnlyList<UserAccount> List() =>
        db.Users
            .AsNoTracking()
            .OrderBy(record => record.Id)
            .ToList()
            .Select(record => PortalJson.Deserialize<UserAccount>(
                record.Payload))
            .ToList();

    public UserAccount? Get(string id)
    {
        var record = db.Users
            .AsNoTracking()
            .SingleOrDefault(item => item.Id == id);

        return record is null
            ? null
            : PortalJson.Deserialize<UserAccount>(record.Payload);
    }

    public UserAccount Add(string name, string email, string role)
    {
        var user = new UserAccount
        {
            Id = $"USR-{Guid.NewGuid():N}"[..10],
            Name = name.Trim(),
            Email = email.Trim(),
            Role = role
        };

        db.Users.Add(new UserRecord
        {
            Id = user.Id,
            Payload = PortalJson.Serialize(user),
            UpdatedAt = clock.UtcNow
        });
        db.SaveChanges();

        return user;
    }

    public void Save(UserAccount user)
    {
        var record = db.Users
            .SingleOrDefault(item => item.Id == user.Id);

        if (record is null)
            return;

        record.Payload = PortalJson.Serialize(user);
        record.UpdatedAt = clock.UtcNow;
        db.SaveChanges();
    }
}
