using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;

/// <summary>
/// The one place that declares what each platform service key needs. Adding an
/// integration means adding an entry here (plus its provider row and runtime adapter);
/// the admin form, validation, storage and verification badge all follow from it.
/// </summary>
public static class ServiceKeyDefinitionRegistry
{
    private static ServiceKeyFieldDefinition ApiKeyField(string placeholder) =>
        new("apiKey", "API key", ServiceKeyFieldKinds.Secret, Placeholder: placeholder);

    private static readonly ServiceKeyFieldOption[] AwsRegions =
    [
        new("eu-west-2", "Europe (London) — eu-west-2"),
        new("eu-west-1", "Europe (Ireland) — eu-west-1"),
        new("eu-central-1", "Europe (Frankfurt) — eu-central-1"),
        new("us-east-1", "US East (N. Virginia) — us-east-1"),
        new("us-east-2", "US East (Ohio) — us-east-2"),
        new("us-west-2", "US West (Oregon) — us-west-2"),
        new("ap-south-1", "Asia Pacific (Mumbai) — ap-south-1"),
        new("ap-southeast-1", "Asia Pacific (Singapore) — ap-southeast-1"),
        new("ap-northeast-1", "Asia Pacific (Tokyo) — ap-northeast-1")
    ];

    public static IReadOnlyList<ServiceKeyDefinition> All { get; } =
    [
        new(PlatformServiceKeyCatalog.Resend, ServiceKeyVerificationMode.Live,
            [ApiKeyField("re_…")]),

        new(PlatformServiceKeyCatalog.Sendgrid, ServiceKeyVerificationMode.Live,
            [ApiKeyField("SG.…")]),

        new(PlatformServiceKeyCatalog.Cloudflare, ServiceKeyVerificationMode.FormatOnly,
            [ApiKeyField("Cloudflare API token")]),

        new(PlatformServiceKeyCatalog.CloudflareR2, ServiceKeyVerificationMode.FormatOnly,
        [
            new("accountId", "Account ID", ServiceKeyFieldKinds.Text),
            new("bucketName", "Bucket name", ServiceKeyFieldKinds.Text),
            new("accessKeyId", "Access Key ID", ServiceKeyFieldKinds.Text),
            new("secretAccessKey", "Secret Access Key", ServiceKeyFieldKinds.Secret),
            new("endpoint", "Endpoint", ServiceKeyFieldKinds.Url,
                Placeholder: "https://<account-id>.r2.cloudflarestorage.com"),
            new("region", "Region", ServiceKeyFieldKinds.Text, Required: false, DefaultValue: "auto")
        ]),

        new(PlatformServiceKeyCatalog.AwsRekognition, ServiceKeyVerificationMode.Live,
        [
            new("accessKeyId", "Access Key ID", ServiceKeyFieldKinds.Text, Placeholder: "AKIA…"),
            new("secretAccessKey", "Secret Access Key", ServiceKeyFieldKinds.Secret),
            new("region", "Region", ServiceKeyFieldKinds.Select,
                DefaultValue: "eu-west-2", Options: AwsRegions)
        ])
    ];

    public static ServiceKeyDefinition? Find(string serviceKey) =>
        All.FirstOrDefault(d => string.Equals(d.ServiceKey, serviceKey, StringComparison.Ordinal));

    /// <summary>
    /// Turns a create/rotate request into the string that gets encrypted. Structured
    /// <paramref name="fields"/> win; the legacy single <paramref name="apiKey"/> shape is
    /// still accepted so existing scripts and deployed clients keep working.
    /// </summary>
    public static Result<string> BuildCredential(
        string serviceKey,
        string? apiKey,
        IReadOnlyDictionary<string, string>? fields)
    {
        var definition = Find(serviceKey);
        if (definition is null)
            return Result<string>.Failure(
                $"No credential definition exists for service key '{serviceKey}'.", 400);

        if (fields is { Count: > 0 })
            return definition.BuildCredential(fields);

        if (!string.IsNullOrWhiteSpace(apiKey))
            return definition.AcceptRawCredential(apiKey);

        return Result<string>.Failure("Credential fields are required.", 400);
    }
}
