using System.Text.Json;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;

public enum ServiceKeyVerificationMode
{
    Live,
    FormatOnly
}

/// <summary>
/// Describes the credential a platform service key needs: its fields, how it is
/// verified, and how the submitted values become the single string that is encrypted
/// and stored. A one-field definition stores the raw value; a multi-field definition
/// stores a JSON object keyed by field name, so existing single-secret rows are unchanged.
/// </summary>
public sealed class ServiceKeyDefinition
{
    private const int MaxValueLength = 4096;

    public ServiceKeyDefinition(
        string serviceKey,
        ServiceKeyVerificationMode verification,
        IReadOnlyList<ServiceKeyFieldDefinition> fields)
    {
        if (fields.Count == 0)
            throw new ArgumentException("A service key definition needs at least one field.", nameof(fields));

        ServiceKey = serviceKey;
        Verification = verification;
        Fields = fields;
    }

    public string ServiceKey { get; }
    public ServiceKeyVerificationMode Verification { get; }
    public IReadOnlyList<ServiceKeyFieldDefinition> Fields { get; }
    public bool IsBundle => Fields.Count > 1;

    /// <summary>Validates submitted field values and returns the string to encrypt.</summary>
    public Result<string> BuildCredential(IReadOnlyDictionary<string, string> values)
    {
        foreach (var name in values.Keys)
        {
            if (!Fields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
                return Result<string>.Failure($"Unknown field '{name}' for '{ServiceKey}'.", 400);
        }

        var normalized = new Dictionary<string, string>(Fields.Count, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            values.TryGetValue(field.Name, out var raw);
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
                value = field.DefaultValue;

            if (string.IsNullOrEmpty(value))
            {
                if (field.Required)
                    return Result<string>.Failure($"{field.Label} is required.", 400);
                continue;
            }

            if (value.Length > MaxValueLength)
                return Result<string>.Failure($"{field.Label} is too long.", 400);

            var fieldError = ValidateValue(field, value);
            if (fieldError is not null)
                return Result<string>.Failure(fieldError, 400);

            normalized[field.Name] = value;
        }

        return Result<string>.Success(
            IsBundle ? JsonSerializer.Serialize(normalized) : normalized[Fields[0].Name]);
    }

    /// <summary>
    /// Accepts a credential already in stored form (the pre-existing single "apiKey"
    /// request shape, or a hand-written JSON bundle) and validates it like submitted fields.
    /// </summary>
    public Result<string> AcceptRawCredential(string raw)
    {
        var values = TryParseCredential(raw);
        return values is null
            ? Result<string>.Failure(
                $"The credential for '{ServiceKey}' must be a JSON object with fields: {string.Join(", ", Fields.Select(f => f.Name))}.",
                400)
            : BuildCredential(values);
    }

    /// <summary>Splits a stored credential back into named fields, or null if it does not parse.</summary>
    public IReadOnlyDictionary<string, string>? TryParseCredential(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        if (!IsBundle)
            return new Dictionary<string, string> { [Fields[0].Name] = stored.Trim() };

        try
        {
            using var doc = JsonDocument.Parse(stored);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return null;

                var field = Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                values[field?.Name ?? property.Name] = property.Value.GetString() ?? string.Empty;
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ValidateValue(ServiceKeyFieldDefinition field, string value)
    {
        switch (field.Kind)
        {
            case ServiceKeyFieldKinds.Select:
                if (field.Options is null || !field.Options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal)))
                    return $"{field.Label} must be one of the listed options.";
                break;
            case ServiceKeyFieldKinds.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    return $"{field.Label} must be a valid http(s) URL.";
                break;
        }

        return null;
    }
}
