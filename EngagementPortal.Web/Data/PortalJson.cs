using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngagementPortal.Web.Data;

/// <summary>
/// Serializzazione degli aggregati salvati nel database. Le opzioni sono
/// le stesse usate dalle API (camelCase, enum come stringa) così il
/// payload resta leggibile e stabile.
/// </summary>
public static class PortalJson
{
    public static readonly JsonSerializerOptions Options = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        IgnoreReadOnlyProperties = true
    };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, Options)
        ?? throw new InvalidOperationException(
            $"Payload JSON non valido per {typeof(T).Name}.");
}
