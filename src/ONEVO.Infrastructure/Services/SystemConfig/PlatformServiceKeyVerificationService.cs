using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Platform service key verification.
///
/// Resend and SendGrid keys are verified with a lightweight live provider call that does
/// not send email. Other supported services remain local format-only checks until their
/// provider HTTP clients are wired.
///
/// SECURITY: the plaintext key is inspected in memory only and is NEVER logged.
/// Provider response bodies are never logged or returned.
/// </summary>
public sealed class PlatformServiceKeyVerificationService : IPlatformServiceKeyVerificationService
{
    private const string ResendVerifyUrl = "https://api.resend.com/domains";
    private const string SendGridVerifyUrl = "https://api.sendgrid.com/v3/scopes";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlatformServiceKeyVerificationService> _logger;

    public PlatformServiceKeyVerificationService(
        IHttpClientFactory httpClientFactory,
        ILogger<PlatformServiceKeyVerificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PlatformServiceKeyVerificationResult> VerifyAsync(
        string serviceKey,
        string apiKeyPlaintext,
        CancellationToken ct)
    {
        // SECURITY: Do NOT log apiKeyPlaintext.
        _logger.LogInformation("Service key verification requested for {ServiceKey}", serviceKey);

        var checkedAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(apiKeyPlaintext))
        {
            return new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = "Stored API key is empty."
            };
        }

        return serviceKey switch
        {
            PlatformServiceKeyCatalog.Resend => await VerifyLiveProviderAsync(
                ResendEmailAdapter.HttpClientName,
                ResendVerifyUrl,
                "Resend",
                apiKeyPlaintext,
                checkedAt,
                ct),
            PlatformServiceKeyCatalog.Sendgrid => await VerifyLiveProviderAsync(
                SendGridEmailAdapter.HttpClientName,
                SendGridVerifyUrl,
                "SendGrid",
                apiKeyPlaintext,
                checkedAt,
                ct),
            PlatformServiceKeyCatalog.Cloudflare => FormatOnlyResult(
                serviceKey, apiKeyPlaintext, checkedAt),
            PlatformServiceKeyCatalog.CloudflareR2 => FormatOnlyResult(
                serviceKey, apiKeyPlaintext, checkedAt),
            PlatformServiceKeyCatalog.AwsRekognition => FormatOnlyResult(
                serviceKey, apiKeyPlaintext, checkedAt),
            _ => new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = $"Service key '{serviceKey}' is not supported for verification."
            }
        };
    }

    private static PlatformServiceKeyVerificationResult FormatOnlyResult(
        string serviceKey,
        string apiKeyPlaintext,
        DateTimeOffset checkedAt)
    {
        var success = apiKeyPlaintext.Length >= 8;
        return new PlatformServiceKeyVerificationResult
        {
            Success = success,
            CheckedAt = checkedAt,
            Message = success
                ? "Local format-only verification passed. Live provider check is not wired for this service."
                : $"Local format-only verification failed for service '{serviceKey}'."
        };
    }

    private async Task<PlatformServiceKeyVerificationResult> VerifyLiveProviderAsync(
        string httpClientName,
        string verifyUrl,
        string providerDisplayName,
        string apiKeyPlaintext,
        DateTimeOffset checkedAt,
        CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, verifyUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyPlaintext);

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient(httpClientName);
            response = await client.SendAsync(httpRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "{Provider} key verification request failed before a response: {ExceptionType}.",
                providerDisplayName,
                ex.GetType().Name);
            return new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = $"{providerDisplayName} verification request failed: {ex.GetType().Name}."
            };
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return new PlatformServiceKeyVerificationResult
                {
                    Success = true,
                    CheckedAt = checkedAt,
                    Message = $"{providerDisplayName} API key verified successfully."
                };
            }

            _logger.LogWarning(
                "{Provider} key verification returned {StatusCode}.",
                providerDisplayName,
                (int)response.StatusCode);

            return new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message =
                    $"{providerDisplayName} API rejected the key ({(int)response.StatusCode} {response.StatusCode})."
            };
        }
    }
}
