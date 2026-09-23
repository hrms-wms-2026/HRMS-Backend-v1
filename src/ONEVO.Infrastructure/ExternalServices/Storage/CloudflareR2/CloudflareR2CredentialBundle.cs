using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;

internal sealed record CloudflareR2CredentialBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string AccountId { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Region { get; init; } = "auto";

    public static bool TryParse(string raw, [NotNullWhen(true)] out CloudflareR2CredentialBundle? bundle)
    {
        bundle = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            bundle = JsonSerializer.Deserialize<CloudflareR2CredentialBundle>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (bundle is null
            || string.IsNullOrWhiteSpace(bundle.AccountId)
            || string.IsNullOrWhiteSpace(bundle.BucketName)
            || string.IsNullOrWhiteSpace(bundle.AccessKeyId)
            || string.IsNullOrWhiteSpace(bundle.SecretAccessKey)
            || string.IsNullOrWhiteSpace(bundle.Endpoint))
        {
            bundle = null;
            return false;
        }

        return true;
    }
}
