using EngagementPortal.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Data;

/// <summary>
/// Crea lo schema e inserisce i dati demo al primo avvio. Non usa
/// migrazioni: per questa demo le tabelle sono due e vengono create con
/// EnsureCreated. Se il database esiste già, i dati non vengono toccati.
/// </summary>
public static class DatabaseBootstrap
{
    public static void Initialize(
        PortalDbContext db,
        ILogger logger)
    {
        db.Database.EnsureCreated();

        if (!db.Clients.Any())
        {
            var clients = InMemoryClientRepository.CreateSeed();
            db.Clients.AddRange(clients.Select(client => new ClientRecord
            {
                TenantId = client.TenantId,
                Name = client.Name,
                Payload = PortalJson.Serialize(client),
                UpdatedAt = client.CreatedDate
            }));

            try
            {
                db.SaveChanges();
                logger.LogInformation(
                    "Database inizializzato con {Count} clienti demo.",
                    clients.Count);
            }
            catch (DbUpdateException)
            {
                // Un'altra istanza potrebbe aver già inserito il seed.
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "Seed clienti già presente, nessuna modifica.");
            }
        }

        if (!db.Engagements.Any())
        {
            var engagements = InMemoryEngagementRepository.CreateSeed();
            db.Engagements.AddRange(engagements.Select(engagement =>
                new EngagementRecord
                {
                    Id = engagement.Id,
                    TenantId = engagement.TenantId,
                    Payload = PortalJson.Serialize(engagement),
                    UpdatedAt = engagement.CreatedDate
                }));

            try
            {
                db.SaveChanges();
                logger.LogInformation(
                    "Database inizializzato con {Count} engagement demo.",
                    engagements.Count);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "Seed engagement già presente, nessuna modifica.");
            }
        }
    }
}
