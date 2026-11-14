using Microsoft.EntityFrameworkCore;

namespace EngagementPortal.Web.Data;

/// <summary>
/// Persistenza su Azure SQL. Engagement e clienti sono salvati come
/// aggregati JSON: una riga per engagement e una per cliente. Questo
/// mantiene il dominio esistente e rende semplice la migrazione da demo
/// a database senza introdurre decine di tabelle/owned type.
/// </summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : DbContext(options)
{
    public DbSet<EngagementRecord> Engagements => Set<EngagementRecord>();

    public DbSet<ClientRecord> Clients => Set<ClientRecord>();

    public DbSet<SettingRecord> Settings => Set<SettingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EngagementRecord>(entity =>
        {
            entity.ToTable("Engagements");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).HasMaxLength(64);
            entity.Property(record => record.ClientId)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(record => record.Payload).IsRequired();
            entity.Property(record => record.UpdatedAt);
            entity.HasIndex(record => record.ClientId);
        });

        modelBuilder.Entity<ClientRecord>(entity =>
        {
            entity.ToTable("Clients");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).HasMaxLength(64);
            entity.Property(record => record.Name)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(record => record.Payload).IsRequired();
            entity.Property(record => record.UpdatedAt);
        });

        modelBuilder.Entity<SettingRecord>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(record => record.SettingKey);
            entity.Property(record => record.SettingKey).HasMaxLength(64);
            entity.Property(record => record.Payload).IsRequired();
            entity.Property(record => record.UpdatedAt);
        });
    }
}

public sealed class EngagementRecord
{
    public required string Id { get; set; }
    public required string ClientId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClientRecord
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SettingRecord
{
    public required string SettingKey { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
