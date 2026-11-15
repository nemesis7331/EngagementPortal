using EngagementPortal.Web.Data;
using EngagementPortal.Web.Domain;
using EngagementPortal.Web.Services;

namespace EngagementPortal.Tests.Unit;

public sealed class ActivityTypesTests
{
    private static ActivityTypes NewCatalog() =>
        new(new InMemoryAppSettingsRepository());

    private static ActivityTypeDefinition? Create(
        ActivityTypes catalog,
        string label,
        string shortName,
        bool retest = true,
        params string[] deliverables) =>
        catalog.Create(
            label,
            shortName,
            "Descrizione",
            retest,
            deliverables,
            out _);

    [Fact]
    public void Catalog_HasTheFiveStandardTypes()
    {
        var all = NewCatalog().All();

        Assert.Equal(5, all.Count);
        Assert.All(ActivityTypes.Defaults, standard =>
            Assert.Contains(all, item => item.Key == standard.Key));
        Assert.Equal(
            [DeliverableKinds.Executive, DeliverableKinds.Technical, DeliverableKinds.Activity],
            all.First(item => item.Key == "PenetrationTest").DeliverableTypes);
    }

    [Fact]
    public void Create_AddsTheTypeToTheCatalog()
    {
        var catalog = NewCatalog();
        var created = Create(catalog, "Mobile App Test", "MAT", true, DeliverableKinds.Technical);

        Assert.NotNull(created);
        Assert.Equal("MobileAppTest", created!.Key);
        Assert.Equal("MAT", created.Short);

        var stored = catalog.Get("MobileAppTest");
        Assert.NotNull(stored);
        Assert.Equal("Mobile App Test", stored!.Label);
        Assert.True(stored.RetestPlanned);
        Assert.Equal([DeliverableKinds.Technical], stored.DeliverableTypes);
        Assert.Equal(6, catalog.All().Count);
    }

    [Fact]
    public void Create_DerivesASiglaAndUniqueKeys()
    {
        var catalog = NewCatalog();

        var first = Create(catalog, "Test di sicurezza fisica", string.Empty);
        var second = Create(catalog, "Test di sicurezza fisica", string.Empty);

        Assert.Equal("TestDiSicurezzaFisica", first!.Key);
        Assert.Equal("TE", first.Short);
        Assert.Equal("TestDiSicurezzaFisica2", second!.Key);
    }

    [Fact]
    public void Create_RejectsAnEmptyLabel()
    {
        var catalog = NewCatalog();
        var created = catalog.Create(" ", null, null, true, null, out var error);

        Assert.Null(created);
        Assert.Equal("Serve un'etichetta di almeno due caratteri.", error);
        Assert.Equal(5, catalog.All().Count);
    }

    [Fact]
    public void Create_RejectsALongSigla()
    {
        var catalog = NewCatalog();
        var created = catalog.Create("Tipo lungo", "ABCDE", null, true, null, out var error);

        Assert.Null(created);
        Assert.Equal("La sigla può avere al massimo 4 caratteri.", error);
    }

    [Fact]
    public void Create_RejectsAnUnknownDeliverable()
    {
        var catalog = NewCatalog();
        var created = catalog.Create(
            "Tipo strano",
            "TS",
            null,
            true,
            ["Video"],
            out var error);

        Assert.Null(created);
        Assert.Equal("Tipologia di deliverable non valida: Video.", error);
    }

    [Fact]
    public void Save_UpdatesACustomType()
    {
        var catalog = NewCatalog();
        Create(catalog, "Mobile App Test", "MAT");

        Assert.True(catalog.Save(
            new ActivityTypeDefinition(
                "MobileAppTest",
                "Mobile Application Test",
                "MA",
                "Nuova descrizione",
                false,
                [DeliverableKinds.Executive]),
            out var error));
        Assert.Null(error);

        var stored = catalog.Get("MobileAppTest")!;
        Assert.Equal("Mobile Application Test", stored.Label);
        Assert.Equal("MA", stored.Short);
        Assert.False(stored.RetestPlanned);
        Assert.Equal([DeliverableKinds.Executive], stored.DeliverableTypes);
    }

    [Fact]
    public void Save_DoesNotCreateAnUnknownType()
    {
        var catalog = NewCatalog();

        Assert.False(catalog.Save(
            new ActivityTypeDefinition("Inventato", "IN", "", "", true, []),
            out var error));
        Assert.Equal("Tipo di attività non previsto.", error);
    }

    [Fact]
    public void Delete_RemovesACustomType()
    {
        var catalog = NewCatalog();
        Create(catalog, "Mobile App Test", "MAT");

        Assert.True(catalog.Delete("MobileAppTest", out var error));
        Assert.Null(error);
        Assert.Null(catalog.Get("MobileAppTest"));
        Assert.Equal(5, catalog.All().Count);
    }

    [Fact]
    public void Delete_KeepsTheStandardTypes()
    {
        var catalog = NewCatalog();

        Assert.False(catalog.Delete("PenetrationTest", out var error));
        Assert.Equal("I tipi standard non si eliminano.", error);
        Assert.NotNull(catalog.Get("PenetrationTest"));
    }

    [Fact]
    public void Engagement_UsesTheCustomTypeKeyAndKeepsItInThePayload()
    {
        var engagement = new Engagement
        {
            Id = "ENG-2026-999",
            ClientId = "anthropic",
            ClientName = "Anthropic",
            Name = "App mobile",
            Type = EngagementType.Custom,
            ActivityTypeKey = "MobileAppTest",
            Owner = "Luca Ferri"
        };

        Assert.Equal("MobileAppTest", engagement.TypeKey);

        var back = PortalJson.Deserialize<Engagement>(
            PortalJson.Serialize(engagement));

        Assert.Equal(EngagementType.Custom, back.Type);
        Assert.Equal("MobileAppTest", back.TypeKey);
    }

    [Fact]
    public void Engagement_WithoutACustomKeyFallsBackToTheType()
    {
        var engagement = new Engagement
        {
            Id = "ENG-2026-998",
            ClientId = "anthropic",
            ClientName = "Anthropic",
            Name = "Perimetro",
            Type = EngagementType.PenetrationTest,
            Owner = "Luca Ferri"
        };

        Assert.Equal("PenetrationTest", engagement.TypeKey);
    }
}
