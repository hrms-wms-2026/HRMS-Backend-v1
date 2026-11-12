using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Application.Common.Auditing;

/// <summary>Serializes the explicit audit projections. Only ever given hand-written projections.</summary>
public static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string? Serialize(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, value.GetType(), Options);
}
