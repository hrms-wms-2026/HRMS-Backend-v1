using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Platform service key verification.
///
/// Phase 1 Foundation stub - validates key format only (non-empty, plausible shape per
/// provider) with NO live external network call, matching the payment gateway
/// verification pattern. Replace with real provider HTTP clients (Resend/SendGrid
/// account endpoints, Cloudflare token verify) in a later delivery.
///
/// SECURITY: the plaintext key is inspected in memory only and is NEVER logged.
/// </summary>
public sealed class PlatformServiceKeyVerificationService : IPlatformServiceKeyVerificationService
{
    private readonly ILogger<PlatformServiceKeyVerificationService> _logger;

    public PlatformServiceKeyVerificationService(ILogger<PlatformServiceKeyVerificationService> logger)
        => _logger = logger;

    public Task<PlatformServiceKeyVerificationResult> VerifyAsync(
        string serviceKey,
        string apiKeyPlaintext,
        CancellationToken ct)
    {
        // SECURITY: Do NOT log apiKeyPlaintext.
        _logger.LogInformation("Service key verification requested for {ServiceKey}", serviceKey);

        var checkedAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(apiKeyPlaintext))
        {
            return Task.FromResult(new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = "Stored API key is empty."
            });
        }

        var success = serviceKey switch
        {
            // Resend keys start with "re_", SendGrid with "SG." - accept any non-trivial
            // key to avoid false negatives; format-only check, no live call in Phase 1.
            PlatformServiceKeyCatalog.Resend => apiKeyPlaintext.Length >= 8,
            PlatformServiceKeyCatalog.Sendgrid => apiKeyPlaintext.Length >= 8,
            PlatformServiceKeyCatalog.Cloudflare => apiKeyPlaintext.Length >= 8,
            PlatformServiceKeyCatalog.CloudflareR2 => apiKeyPlaintext.Length >= 8,
            PlatformServiceKeyCatalog.AwsRekognition => apiKeyPlaintext.Length >= 8,
            _ => false
        };

        var message = success
            ? "Key format accepted. Live provider verification is not yet wired (Phase 1 stub)."
            : $"Key format check failed for service '{serviceKey}'.";

        return Task.FromResult(new PlatformServiceKeyVerificationResult
        {
            Success = success,
            CheckedAt = checkedAt,
            Message = message
        });
    }
}
