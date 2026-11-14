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
        string tenantId,
        bool crossTenant)
    {
        var query = db.Engagements.AsNoTracking();

        if (!crossTenant)
            query = query.Where(record => record.TenantId == tenantId);

        return query
            .OrderBy(record => record.Id)
            .ToList()
            .Select(record => PortalJson.Deserialize<Engagement>(
                record.Payload))
            .ToList();
    }

    public Engagement? Get(
        string id,
        string tenantId,
        bool crossTenant)
    {
        var record = FindRecord(id, tenantId, crossTenant);
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
                TenantId = engagement.TenantId,
                Payload = payload,
                UpdatedAt = clock.UtcNow
            });
        }
        else
        {
            record.TenantId = engagement.TenantId;
            record.Payload = payload;
            record.UpdatedAt = clock.UtcNow;
        }

        db.SaveChanges();
    }

    private EngagementRecord? FindRecord(
        string id,
        string tenantId,
        bool crossTenant)
    {
        var query = db.Engagements
            .AsNoTracking()
            .Where(record => record.Id == id);

        if (!crossTenant)
            query = query.Where(record => record.TenantId == tenantId);

        return query.SingleOrDefault();
    }
}
