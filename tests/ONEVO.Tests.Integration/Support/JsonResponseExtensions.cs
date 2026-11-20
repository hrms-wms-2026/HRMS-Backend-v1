using System.Text.Json;

namespace ONEVO.Tests.Integration.Support;

public static class JsonResponseExtensions
{
    private static readonly JsonElement JsonNull = JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>
    /// API responses leave out null properties (Improvements task 10, DEC-070), so a missing
    /// property reads as JSON null here, as it does for the clients.
    /// </summary>
    public static JsonElement GetPropertyOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : JsonNull;
}
