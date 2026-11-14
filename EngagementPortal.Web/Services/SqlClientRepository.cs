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

    public ClientAccount? Get(string tenantId)
    {
        var record = FindRecord(tenantId);
        return record is null
            ? null
            : PortalJson.Deserialize<ClientAccount>(record.Payload);
    }

    public bool Exists(string tenantId) =>
        db.Clients.AsNoTracking().Any(record =>
            record.TenantId == tenantId);

    public ClientAccount Add(string name, string? tenantId)
    {
        var resolved = string.IsNullOrWhiteSpace(tenantId)
            ? UniqueTenantId(Slug(name))
            : tenantId.Trim();

        var account = new ClientAccount
        {
            TenantId = resolved,
            Name = name.Trim()
        };

        db.Clients.Add(new ClientRecord
        {
            TenantId = account.TenantId,
            Name = account.Name,
            Payload = PortalJson.Serialize(account),
            UpdatedAt = clock.UtcNow
        });
        db.SaveChanges();

        return account;
    }

    public ClientAccount? Rename(string tenantId, string name)
    {
        var record = FindRecord(tenantId);

        if (record is null)
            return null;

        var account = PortalJson.Deserialize<ClientAccount>(
            record.Payload);
        account.Name = name.Trim();

        Save(record, account);
        return account;
    }

    public ClientUser? AddUser(
        string tenantId,
        string name,
        string email)
    {
        var record = FindRecord(tenantId);

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

    public bool RemoveUser(string tenantId, string userId)
    {
        var record = FindRecord(tenantId);

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

    private ClientRecord? FindRecord(string tenantId) =>
        db.Clients.SingleOrDefault(record =>
            record.TenantId == tenantId);

    private void Save(
        ClientRecord record,
        ClientAccount account)
    {
        record.Name = account.Name;
        record.Payload = PortalJson.Serialize(account);
        record.UpdatedAt = clock.UtcNow;
        db.SaveChanges();
    }

    private string UniqueTenantId(string slug)
    {
        var candidate = $"tenant-{slug}";
        var index = 2;

        while (Exists(candidate))
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

        var slug = new string(chars).Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(slug)
            ? "cliente"
            : slug;
    }
}
