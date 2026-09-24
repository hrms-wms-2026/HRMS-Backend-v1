using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

/// <summary>
/// Versioned composite credential stored encrypted in platform_service_keys.service_key = aws_rekognition.
/// Same contract as Cloudflare R2: one JSON object, never plaintext columns.
/// </summary>
internal sealed record AwsRekognitionCredentialBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string LivenessRoleArn { get; init; } = string.Empty;

    public static bool TryParse(string raw, [NotNullWhen(true)] out AwsRekognitionCredentialBundle? bundle)
    {
        bundle = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            bundle = JsonSerializer.Deserialize<AwsRekognitionCredentialBundle>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return bundle is not null;
    }

    public bool HasAccessKeys =>
        !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
