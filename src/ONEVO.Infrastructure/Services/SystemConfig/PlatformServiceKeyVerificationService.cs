using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;
using ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Platform service key verification.
///
/// Resend, SendGrid, AWS Rekognition, and Cloudflare R2 are verified with a live
/// provider call. Other supported services remain local format-only checks.
///
/// SECURITY: the plaintext key is inspected in memory only and is NEVER logged.
/// Provider response bodies are never logged or returned.
/// </summary>
public sealed class PlatformServiceKeyVerificationService : IPlatformServiceKeyVerificationService
{
    private const string ResendVerifyUrl = "https://api.resend.com/domains";
    private const string SendGridVerifyUrl = "https://api.sendgrid.com/v3/scopes";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAwsRekognitionConnectionProbe _rekognitionProbe;
    private readonly ICloudflareR2ConnectionProbe _r2Probe;
    private readonly ILogger<PlatformServiceKeyVerificationService> _logger;

    public PlatformServiceKeyVerificationService(
        IHttpClientFactory httpClientFactory,
        IAwsRekognitionConnectionProbe rekognitionProbe,
        ICloudflareR2ConnectionProbe r2Probe,
        ILogger<PlatformServiceKeyVerificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _rekognitionProbe = rekognitionProbe;
        _r2Probe = r2Probe;
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
            PlatformServiceKeyCatalog.CloudflareR2 => await VerifyCloudflareR2Async(
                apiKeyPlaintext, checkedAt, ct),
            PlatformServiceKeyCatalog.AwsRekognition => await VerifyAwsRekognitionBundleAsync(
                apiKeyPlaintext, checkedAt, ct),
            _ => new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = $"Service key '{serviceKey}' is not supported for verification."
            }
        };
    }

    private async Task<PlatformServiceKeyVerificationResult> VerifyAwsRekognitionBundleAsync(
        string apiKeyPlaintext,
        DateTimeOffset checkedAt,
        CancellationToken ct)
    {
        if (!AwsRekognitionCredentialBundle.TryParse(apiKeyPlaintext, out var bundle) || !bundle.HasAccessKeys)
        {
            return new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = "AWS Rekognition key must be Access Key ID, Secret Access Key, and region."
            };
        }

        var region = string.IsNullOrWhiteSpace(bundle.Region) ? "us-east-1" : bundle.Region.Trim();
        var probe = await _rekognitionProbe.ProbeAsync(
            bundle.AccessKeyId.Trim(), bundle.SecretAccessKey.Trim(), region, ct);

        return new PlatformServiceKeyVerificationResult
        {
            Success = probe.Success,
            CheckedAt = checkedAt,
            Message = probe.Message,
            Identity = probe.Identity,
            Region = probe.Region ?? region,
            Service = probe.Success ? "Amazon Rekognition" : null
        };
    }

    private async Task<PlatformServiceKeyVerificationResult> VerifyCloudflareR2Async(
        string storedCredential,
        DateTimeOffset checkedAt,
        CancellationToken ct)
    {
        if (!CloudflareR2CredentialBundle.TryParse(storedCredential, out var bundle))
        {
            return new PlatformServiceKeyVerificationResult
            {
                Success = false,
                CheckedAt = checkedAt,
                Message = "Cloudflare R2 key must include account, bucket, access key, secret, and endpoint."
            };
        }

        var probe = await _r2Probe.ProbeAsync(
            bundle.AccessKeyId.Trim(),
            bundle.SecretAccessKey.Trim(),
            bundle.BucketName.Trim(),
            bundle.Endpoint.Trim(),
            bundle.Region,
            ct);

        return new PlatformServiceKeyVerificationResult
        {
            Success = probe.Success,
            CheckedAt = checkedAt,
            Message = probe.Message,
            Identity = probe.Success ? probe.Bucket : null,
            Region = probe.Region,
            Service = probe.Success ? "Cloudflare R2" : null
        };
    }

    private static PlatformServiceKeyVerificationResult BundleFormatResult(
        string serviceKey,
        string storedCredential,
        DateTimeOffset checkedAt)
    {
        var check = ServiceKeyDefinitionRegistry.Find(serviceKey)?.AcceptRawCredential(storedCredential);
        var success = check?.IsSuccess == true;
        return new PlatformServiceKeyVerificationResult
        {
            Success = success,
            CheckedAt = checkedAt,
            Message = success
                ? "Local format-only verification passed. Live provider check is not wired for this service."
                : $"Stored credential for '{serviceKey}' is incomplete or malformed: {check?.Error}"
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
