using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EngagementPortal.Tests.Unit;

public sealed class SqlRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private sealed class FixedDateProvider : IDateProvider
    {
        public DateOnly Today => SqlRepositoryTests.Today;

        public DateTimeOffset UtcNow =>
            new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static (SqliteConnection Connection, PortalDbContext Db)
        NewDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();

        return (connection, db);
    }

    private static Engagement NewEngagement() => new()
    {
        Id = "ENG-2026-001",
        ClientId = "contoso",
        ClientName = "Contoso",
        Name = "External Penetration Test",
        Type = EngagementType.PenetrationTest,
        Owner = "Provider Pentester",
        HasManleva = true,
        OrderConfirmed = true,
        Scope = new ScopeData
        {
            Host = "app.contoso.example",
            Contact = "security@contoso.example",
            Done = true,
            By = "Client",
            Date = DateTimeOffset.UtcNow
        },
        Credentials = new CredentialData
        {
            Vpn = "vpn.contoso.example",
            Done = true,
            By = "Client"
        }
    };

    [Fact]
    public void Engagement_RoundTripsThroughSql()
    {
        var (connection, db) = NewDatabase();
        using var _ = connection;

        var engagement = NewEngagement();
        engagement.Proposals.Add(new Proposal
        {
            Id = "P-001",
            From = ProposalParty.Provider,
            By = "Luca Ferri",
            StartDate = Today.AddDays(2),
            EndDate = Today.AddDays(5),
            Status = ProposalStatus.Accepted
        });
        engagement.Reports.Add(new ReportFile(
            "R-001",
            "report.pdf",
            "PDF",
            1024,
            DateTimeOffset.UtcNow,
            false));

        var repository = new SqlEngagementRepository(
            db,
            new FixedDateProvider());
        repository.Save(engagement);

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(connection)
            .Options;
        using var secondDb = new PortalDbContext(options);
        var secondRepository = new SqlEngagementRepository(
            secondDb,
            new FixedDateProvider());

        var loaded = secondRepository.Get(
            engagement.Id,
            engagement.ClientId,
            allClients: false);

        Assert.NotNull(loaded);
        Assert.Equal("External Penetration Test", loaded.Name);
        Assert.Equal("app.contoso.example", loaded.Scope.Host);
        Assert.Equal("vpn.contoso.example", loaded.Credentials.Vpn);
        Assert.Single(loaded.Proposals);
        Assert.Equal(ProposalStatus.Accepted, loaded.Proposals[0].Status);
        Assert.Single(loaded.Reports);

        Assert.Single(secondRepository.List("contoso", false));
        Assert.Empty(secondRepository.List("other", false));
        Assert.Single(secondRepository.List("other", true));
    }

    [Fact]
    public void Client_CrudPersistsThroughSql()
    {
        var (connection, db) = NewDatabase();
        using var _ = connection;

        var repository = new SqlClientRepository(
            db,
            new FixedDateProvider());

        var client = repository.Add("Acme", "https://acme.example");
        Assert.Equal("acme", client.Id);

        var user = repository.AddUser(
            client.Id,
            "Mario Rossi",
            "mario.rossi@acme.example");
        Assert.NotNull(user);

        var renamed = repository.Update(
            client.Id,
            "Acme S.p.A.",
            "https://www.acme.example");
        Assert.NotNull(renamed);
        Assert.Equal("Acme S.p.A.", renamed.Name);
        Assert.Equal("https://www.acme.example", renamed.Website);

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(connection)
            .Options;
        using var secondDb = new PortalDbContext(options);
        var secondRepository = new SqlClientRepository(
            secondDb,
            new FixedDateProvider());

        var loaded = secondRepository.Get("acme");
        Assert.NotNull(loaded);
        Assert.Equal("Acme S.p.A.", loaded.Name);
        Assert.Equal("https://www.acme.example", loaded.Website);
        Assert.Single(loaded.Users);

        Assert.True(secondRepository.RemoveUser(
            "acme",
            loaded.Users[0].Id));
        Assert.Empty(secondRepository.Get("acme")!.Users);
        Assert.True(secondRepository.Exists("acme"));
    }

    [Fact]
    public void SqlServerModel_GeneratesSchemaScript()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=EngagementPortal;" +
                "Trusted_Connection=True;")
            .Options;

        using var db = new PortalDbContext(options);
        var script = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE [Engagements]", script);
        Assert.Contains("CREATE TABLE [Clients]", script);
        Assert.Contains("[Payload] nvarchar(max)", script);
    }

    [Fact]
    public void Bootstrap_SeedsDemoDataOnlyOnce()
    {
        var (connection, db) = NewDatabase();
        using var _ = connection;

        using var loggerFactory = LoggerFactory.Create(_ => { });
        var logger = loggerFactory.CreateLogger("test");

        DatabaseBootstrap.Initialize(db, logger);
        DatabaseBootstrap.Initialize(db, logger);

        Assert.Equal(7, db.Engagements.Count());
        Assert.Equal(4, db.Clients.Count());
    }
}
