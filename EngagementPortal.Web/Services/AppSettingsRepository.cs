using System.Collections.Concurrent;
using EngagementPortal.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Impostazioni applicative (per ora i template email standard) salvate
/// come payload JSON, con la stessa forma dei repository di dominio.
/// </summary>
public interface IAppSettingsRepository
{
    T? Get<T>(string key) where T : class;

    void Save<T>(string key, T value) where T : class;
}

public sealed class InMemoryAppSettingsRepository : IAppSettingsRepository
{
    private readonly ConcurrentDictionary<string, string> values = new();

    public T? Get<T>(string key) where T : class =>
        values.TryGetValue(key, out var payload)
            ? PortalJson.Deserialize<T>(payload)
            : null;

    public void Save<T>(string key, T value) where T : class =>
        values[key] = PortalJson.Serialize(value);
}

public sealed class SqlAppSettingsRepository(
    PortalDbContext db,
    IDateProvider clock) : IAppSettingsRepository
{
    public T? Get<T>(string key) where T : class
    {
        var record = db.Settings
            .AsNoTracking()
            .FirstOrDefault(item => item.SettingKey == key);

        return record is null
            ? null
            : PortalJson.Deserialize<T>(record.Payload);
    }

    public void Save<T>(string key, T value) where T : class
    {
        var payload = PortalJson.Serialize(value);
        var record = db.Settings.Find(key);

        if (record is null)
        {
            db.Settings.Add(new SettingRecord
            {
                SettingKey = key,
                Payload = payload,
                UpdatedAt = clock.UtcNow
            });
        }
        else
        {
            record.Payload = payload;
            record.UpdatedAt = clock.UtcNow;
        }

        db.SaveChanges();
    }
}
