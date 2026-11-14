using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Implementazione SQL di <see cref="IClientRepository"/>.
/// Ogni cliente (con le utenze associate) è un aggregato JSON.
/// </summary>
public sealed class SqlClientRepository(
    PortalDbContext db,
    IDateProvider clock) : IClientRepository
{
    public IReadOnlyList<ClientAccount> List() =>
        db.Clients
            .AsNoTracking()
            .OrderBy(record => record.Name)
            .ToList()
            .Select(record => PortalJson.Deserialize<ClientAccount>(
                record.Payload))
            .ToList();

    public ClientAccount? Get(string clientId)
    {
        var record = FindRecord(clientId);
        return record is null
            ? null
            : PortalJson.Deserialize<ClientAccount>(record.Payload);
    }

    public bool Exists(string clientId) =>
        db.Clients.AsNoTracking().Any(record =>
            record.Id == clientId);

    public ClientAccount Add(string name, string? website)
    {
        var account = new ClientAccount
        {
            Id = UniqueId(Slug(name)),
            Name = name.Trim(),
            Website = website
        };

        db.Clients.Add(new ClientRecord
        {
            Id = account.Id,
            Name = account.Name,
            Payload = PortalJson.Serialize(account),
            UpdatedAt = clock.UtcNow
        });
        db.SaveChanges();

        return account;
    }

    public ClientAccount? Update(
        string clientId,
        string name,
        string? website)
    {
        var record = FindRecord(clientId);

        if (record is null)
            return null;

        var account = PortalJson.Deserialize<ClientAccount>(
            record.Payload);
        account.Name = name.Trim();
        account.Website = website;

        Save(record, account);
        return account;
    }

    public void Save(ClientAccount account)
    {
        var record = FindRecord(account.Id);

        if (record is not null)
            Save(record, account);
    }

    public ClientUser? AddUser(
        string clientId,
        string name,
        string email)
    {
        var record = FindRecord(clientId);

        if (record is null)
            return null;

        var account = PortalJson.Deserialize<ClientAccount>(
            record.Payload);

        var user = new ClientUser(
            $"CU-{Guid.NewGuid():N}"[..10],
            name.Trim(),
            email.Trim());

        account.Users.Add(user);
        Save(record, account);

        return user;
    }

    public bool RemoveUser(string clientId, string userId)
    {
        var record = FindRecord(clientId);

        if (record is null)
            return false;

        var account = PortalJson.Deserialize<ClientAccount>(
            record.Payload);
        var user = account.Users
            .FirstOrDefault(item => item.Id == userId);

        if (user is null)
            return false;

        account.Users.Remove(user);
        Save(record, account);

        return true;
    }

    private ClientRecord? FindRecord(string clientId) =>
        db.Clients.SingleOrDefault(record => record.Id == clientId);

    private void Save(
        ClientRecord record,
        ClientAccount account)
    {
        record.Name = account.Name;
        record.Payload = PortalJson.Serialize(account);
        record.UpdatedAt = clock.UtcNow;
        db.SaveChanges();
    }

    private string UniqueId(string slug)
    {
        var candidate = slug;
        var index = 2;

        while (Exists(candidate))
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

        var slug = new string(chars).Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(slug)
            ? "cliente"
            : slug;
    }
}
