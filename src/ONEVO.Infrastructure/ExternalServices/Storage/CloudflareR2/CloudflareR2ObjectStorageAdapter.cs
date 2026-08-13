using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;

public sealed class CloudflareR2ObjectStorageAdapter : IObjectStorageAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPlatformServiceKeyResolver _keyResolver;
    private readonly ILogger<CloudflareR2ObjectStorageAdapter> _logger;
    private AmazonS3Client? _client;
    private string? _bucketName;
    private string? _endpointHost;

    public CloudflareR2ObjectStorageAdapter(
        IPlatformServiceKeyResolver keyResolver,
        ILogger<CloudflareR2ObjectStorageAdapter> logger)
    {
        _keyResolver = keyResolver;
        _logger = logger;
    }

    public async Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var (client, bucketName) = await GetClientAsync(ct);

        // Cloudflare R2 does not implement the AWS SDK's default chunked
        // streaming payload (with or without a trailing checksum) for PUT
        // requests — it returns 501 NotImplemented ("STREAMING-AWS4-HMAC-
        // SHA256-PAYLOAD[-TRAILER] not implemented") regardless of
        // credentials. Forcing single-shot, non-chunked upload keeps PUT
        // requests compatible with R2's S3-compatible API surface.
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            UseChunkEncoding = false
        };

        try
        {
            await client.PutObjectAsync(request, ct);
        }
        catch (AmazonS3Exception ex)
        {
            LogR2Failure(ex, "PutObject", bucketName);
            throw new ObjectStorageException("Failed to upload object to Cloudflare R2.", ex);
        }
    }

    public async Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        var (client, bucketName) = await GetClientAsync(ct);

        try
        {
            await client.DeleteObjectAsync(bucketName, objectKey, ct);
        }
        catch (AmazonS3Exception ex)
        {
            LogR2Failure(ex, "DeleteObject", bucketName);
            throw new ObjectStorageException("Failed to delete object from Cloudflare R2.", ex);
        }
    }

    public async Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        var (client, bucketName) = await GetClientAsync(ct);

        try
        {
            var response = await client.GetObjectAsync(bucketName, objectKey, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            LogR2Failure(ex, "GetObject", bucketName);
            throw new ObjectStorageException("Failed to read object from Cloudflare R2.", ex);
        }
    }

    public async Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default)
    {
        var (client, bucketName) = await GetClientAsync(ct);

        try
        {
            await client.GetObjectMetadataAsync(bucketName, objectKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            LogR2Failure(ex, "GetObjectMetadata", bucketName);
            throw new ObjectStorageException("Failed to check object existence in Cloudflare R2.", ex);
        }
    }

    public async Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
    {
        var (client, bucketName) = await GetClientAsync(ct);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        };

        try
        {
            return client.GetPreSignedURL(request);
        }
        catch (AmazonS3Exception ex)
        {
            LogR2Failure(ex, "GetPreSignedURL", bucketName);
            throw new ObjectStorageException("Failed to generate signed URL for Cloudflare R2 object.", ex);
        }
    }

    // SECURITY: logs only the status/error code, request id, and sanitized
    // endpoint host/bucket name — never the access key, secret key, or any
    // other credential material.
    private void LogR2Failure(AmazonS3Exception ex, string operation, string bucketName)
    {
        _logger.LogError(
            ex,
            "Cloudflare R2 {Operation} failed. StatusCode={StatusCode} ErrorCode={ErrorCode} RequestId={RequestId} Endpoint={EndpointHost} Bucket={BucketName}",
            operation,
            ex.StatusCode,
            ex.ErrorCode,
            ex.RequestId,
            _endpointHost,
            bucketName);
    }

    private async Task<(AmazonS3Client Client, string BucketName)> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null && _bucketName is not null)
        {
            return (_client, _bucketName);
        }

        var rawValue = await _keyResolver.ResolveActiveKeyAsync(PlatformServiceKeyCatalog.CloudflareR2, ct);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new ObjectStorageException("Cloudflare R2 is not configured. No active platform service key found.");
        }

        CloudflareR2CredentialBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<CloudflareR2CredentialBundle>(rawValue, JsonOptions);
        }
        catch (JsonException)
        {
            throw new ObjectStorageException("Cloudflare R2 credential bundle is not valid JSON.");
        }

        if (bundle is null
            || string.IsNullOrWhiteSpace(bundle.AccountId)
            || string.IsNullOrWhiteSpace(bundle.BucketName)
            || string.IsNullOrWhiteSpace(bundle.AccessKeyId)
            || string.IsNullOrWhiteSpace(bundle.SecretAccessKey)
            || string.IsNullOrWhiteSpace(bundle.Endpoint))
        {
            throw new ObjectStorageException("Cloudflare R2 credential bundle is missing required fields.");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = bundle.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = string.IsNullOrWhiteSpace(bundle.Region) ? "auto" : bundle.Region
        };

        _client = new AmazonS3Client(bundle.AccessKeyId, bundle.SecretAccessKey, config);
        _bucketName = bundle.BucketName;
        _endpointHost = Uri.TryCreate(bundle.Endpoint, UriKind.Absolute, out var endpointUri)
            ? endpointUri.Host
            : "(unparsable)";

        return (_client, _bucketName);
    }
}
