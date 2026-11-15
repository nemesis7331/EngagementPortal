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

        // Le installazioni esistenti non ricevono le nuove tabelle da
        // EnsureCreated: le tabelle delle impostazioni e degli utenti
        // vengono create qui.
        EnsureSettingsTable(db, logger);
        EnsureUsersTable(db, logger);

        if (!db.Clients.Any())
        {
            var clients = InMemoryClientRepository.CreateSeed();
            db.Clients.AddRange(clients.Select(client => new ClientRecord
            {
                Id = client.Id,
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
                    ClientId = engagement.ClientId,
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

        if (!db.Users.Any())
        {
            var users = InMemoryUserRepository.CreateSeed();
            db.Users.AddRange(users.Select(user => new UserRecord
            {
                Id = user.Id,
                Payload = PortalJson.Serialize(user),
                UpdatedAt = user.CreatedDate
            }));

            try
            {
                db.SaveChanges();
                logger.LogInformation(
                    "Database inizializzato con {Count} utenti demo.",
                    users.Count);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "Seed utenti già presente, nessuna modifica.");
            }
        }
    }

    private static void EnsureSettingsTable(
        PortalDbContext db,
        ILogger logger)
    {
        try
        {
            var provider = db.Database.ProviderName ?? string.Empty;

            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                db.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS Settings (" +
                    "SettingKey TEXT NOT NULL PRIMARY KEY, " +
                    "Payload TEXT NOT NULL, " +
                    "UpdatedAt TEXT NOT NULL);");
            }
            else
            {
                db.Database.ExecuteSqlRaw(
                    "IF OBJECT_ID(N'Settings', N'U') IS NULL " +
                    "CREATE TABLE Settings (" +
                    "SettingKey nvarchar(64) NOT NULL PRIMARY KEY, " +
                    "Payload nvarchar(max) NOT NULL, " +
                    "UpdatedAt datetimeoffset NOT NULL);");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Impossibile verificare la tabella Settings: i template " +
                "standard resteranno in memoria.");
        }
    }

    private static void EnsureUsersTable(
        PortalDbContext db,
        ILogger logger)
    {
        try
        {
            var provider = db.Database.ProviderName ?? string.Empty;

            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                db.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS Users (" +
                    "Id TEXT NOT NULL PRIMARY KEY, " +
                    "Payload TEXT NOT NULL, " +
                    "UpdatedAt TEXT NOT NULL);");
            }
            else
            {
                db.Database.ExecuteSqlRaw(
                    "IF OBJECT_ID(N'Users', N'U') IS NULL " +
                    "CREATE TABLE Users (" +
                    "Id nvarchar(64) NOT NULL PRIMARY KEY, " +
                    "Payload nvarchar(max) NOT NULL, " +
                    "UpdatedAt datetimeoffset NOT NULL);");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Impossibile verificare la tabella Users: la sezione " +
                "Utenti resterà in memoria.");
        }
    }
}
