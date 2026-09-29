using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;

public sealed record CloudflareR2ProbeResult(
    bool Success,
    string Message,
    string? Bucket,
    string? Region);

/// <summary>
/// Live check that the stored R2 key can see its bucket. Uses HeadBucket only —
/// it does not upload or list objects.
/// SECURITY: never logs the access key or secret.
/// </summary>
public interface ICloudflareR2ConnectionProbe
{
    Task<CloudflareR2ProbeResult> ProbeAsync(
        string accessKeyId,
        string secretAccessKey,
        string bucketName,
        string endpoint,
        string region,
        CancellationToken ct);
}

public sealed class CloudflareR2ConnectionProbe : ICloudflareR2ConnectionProbe
{
    private readonly ILogger<CloudflareR2ConnectionProbe> _logger;

    public CloudflareR2ConnectionProbe(ILogger<CloudflareR2ConnectionProbe> logger)
        => _logger = logger;

    public async Task<CloudflareR2ProbeResult> ProbeAsync(
        string accessKeyId,
        string secretAccessKey,
        string bucketName,
        string endpoint,
        string region,
        CancellationToken ct)
    {
        var resolvedRegion = string.IsNullOrWhiteSpace(region) ? "auto" : region.Trim();
        if (!IsCloudflareR2Endpoint(endpoint))
        {
            return new CloudflareR2ProbeResult(
                false,
                "Cloudflare R2 endpoint must be an https host on r2.cloudflarestorage.com.",
                bucketName,
                resolvedRegion);
        }

        try
        {
            using var client = new AmazonS3Client(accessKeyId, secretAccessKey, new AmazonS3Config
            {
                ServiceURL = endpoint.Trim(),
                ForcePathStyle = true,
                AuthenticationRegion = resolvedRegion
            });
            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = bucketName }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden
            || ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "Cloudflare R2 HeadBucket rejected the credentials. StatusCode={StatusCode} ErrorCode={ErrorCode}",
                (int)ex.StatusCode,
                ex.ErrorCode);
            return new CloudflareR2ProbeResult(
                false,
                "Cloudflare R2 rejected the credentials. Check the access key, secret, and bucket.",
                bucketName,
                resolvedRegion);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Cloudflare R2 HeadBucket did not find the bucket. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return new CloudflareR2ProbeResult(
                false,
                "Cloudflare R2 did not find that bucket for this account.",
                bucketName,
                resolvedRegion);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogWarning(
                "Cloudflare R2 HeadBucket failed. StatusCode={StatusCode} ErrorCode={ErrorCode}",
                (int)ex.StatusCode,
                ex.ErrorCode);
            return new CloudflareR2ProbeResult(
                false,
                $"Cloudflare R2 verification failed ({(int)ex.StatusCode} {ex.StatusCode}).",
                bucketName,
                resolvedRegion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Cloudflare R2 HeadBucket failed before a response. ExceptionType={ExceptionType}",
                ex.GetType().Name);
            return new CloudflareR2ProbeResult(
                false,
                "Could not reach Cloudflare R2 to verify the credentials.",
                bucketName,
                resolvedRegion);
        }

        return new CloudflareR2ProbeResult(
            true,
            "Cloudflare R2 bucket verified.",
            bucketName,
            resolvedRegion);
    }

    private static bool IsCloudflareR2Endpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;
        return uri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase);
    }
}
