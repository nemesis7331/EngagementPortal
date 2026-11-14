using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Services;

/// <summary>
/// Implementazione SQL di <see cref="IEngagementRepository"/>.
/// Ogni engagement è un aggregato JSON nella tabella Engagements.
/// </summary>
public sealed class SqlEngagementRepository(
    PortalDbContext db,
    IDateProvider clock) : IEngagementRepository
{
    public IReadOnlyList<Engagement> List(
        string clientId,
        bool allClients)
    {
        var query = db.Engagements.AsNoTracking();

        if (!allClients)
            query = query.Where(record => record.ClientId == clientId);

        return query
            .OrderBy(record => record.Id)
            .ToList()
            .Select(record => PortalJson.Deserialize<Engagement>(
                record.Payload))
            .ToList();
    }

    public Engagement? Get(
        string id,
        string clientId,
        bool allClients)
    {
        var record = FindRecord(id, clientId, allClients);
        return record is null
            ? null
            : PortalJson.Deserialize<Engagement>(record.Payload);
    }

    public void Save(Engagement engagement)
    {
        var payload = PortalJson.Serialize(engagement);
        var record = db.Engagements.Find(engagement.Id);

        if (record is null)
        {
            db.Engagements.Add(new EngagementRecord
            {
                Id = engagement.Id,
                ClientId = engagement.ClientId,
                Payload = payload,
                UpdatedAt = clock.UtcNow
            });
        }
        else
        {
            record.ClientId = engagement.ClientId;
            record.Payload = payload;
            record.UpdatedAt = clock.UtcNow;
        }

        db.SaveChanges();
    }

    private EngagementRecord? FindRecord(
        string id,
        string clientId,
        bool allClients)
    {
        var query = db.Engagements
            .AsNoTracking()
            .Where(record => record.Id == id);

        if (!allClients)
            query = query.Where(record => record.ClientId == clientId);

        return query.SingleOrDefault();
    }
}
