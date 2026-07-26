namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

/// <summary>
/// Metadata-only provider card managed by ONEVO.
/// Credentials are stored only in the family-specific credential tables.
/// </summary>
public sealed class PlatformProvider
{
    public Guid Id { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProviderFamily { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class PlatformProviderFamilies
{
    public const string OAuthApp = "oauth_app";
    public const string TransactionalEmail = "transactional_email";
    public const string Infrastructure = "infrastructure";
    public const string ObjectStorage = "object_storage";
    public const string AiVerification = "ai_verification";
    public const string PaymentGateway = "payment_gateway";

    public static readonly IReadOnlyList<string> All =
    [
        OAuthApp,
        TransactionalEmail,
        Infrastructure,
        ObjectStorage,
        AiVerification,
        PaymentGateway
    ];

    public static readonly IReadOnlySet<string> PlatformServiceKeyFamilies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            TransactionalEmail,
            Infrastructure,
            ObjectStorage,
            AiVerification
        };
}
