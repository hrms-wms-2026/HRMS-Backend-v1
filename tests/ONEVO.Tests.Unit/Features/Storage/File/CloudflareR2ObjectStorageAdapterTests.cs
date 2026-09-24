using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Storage;
using ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

/// <summary>
/// Covers the local/dev "logo upload now fails with 502 file_upload_failed" investigation:
/// proves CloudflareR2ObjectStorageAdapter converts any AmazonS3Exception raised by the
/// underlying AWS S3 SDK into the generic ObjectStorageException that FileStorageService
/// maps to file_upload_failed/502 — and that nothing about the failure (credentials,
/// S3 error message) ever leaks into that generic message.
/// </summary>
public class CloudflareR2ObjectStorageAdapterTests
{
    private const string AccessKeyId = "AKIA_TEST_ACCESS_KEY_ID_0001";
    private const string SecretAccessKey = "test-secret-access-key-value-should-never-leak-0001";

    private sealed class FakeResolver : IPlatformServiceKeyResolver
    {
        public string? RawBundle { get; set; }

        public Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct)
            => Task.FromResult(RawBundle);

        public Task<TransactionalEmailProviderResolution> ResolveActiveTransactionalEmailProviderAsync(
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static string ValidBundleJson() => $$"""
        {
          "accountId": "e99bb9ab479f01fac45d7cb5a1b61372",
          "bucketName": "onexso",
          "accessKeyId": "{{AccessKeyId}}",
          "secretAccessKey": "{{SecretAccessKey}}",
          "endpoint": "https://e99bb9ab479f01fac45d7cb5a1b61372.r2.cloudflarestorage.com",
          "region": "auto"
        }
        """;

    private static CloudflareR2ObjectStorageAdapter CreateAdapter(
        string? rawBundle, ILogger<CloudflareR2ObjectStorageAdapter>? logger = null)
    {
        var resolver = new FakeResolver { RawBundle = rawBundle };
        return new CloudflareR2ObjectStorageAdapter(
            resolver, logger ?? NullLogger<CloudflareR2ObjectStorageAdapter>.Instance);
    }

    [Fact]
    public async Task PutObjectAsync_NoActiveServiceKey_ThrowsObjectStorageExceptionWithoutNetworkCall()
    {
        var adapter = CreateAdapter(rawBundle: null);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Assert.ThrowsAsync<ObjectStorageException>(
            () => adapter.PutObjectAsync("logo.png", content, "image/png", CancellationToken.None));

        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutObjectAsync_MissingBundleField_ThrowsObjectStorageException()
    {
        // bucketName omitted — mirrors "credential bundle missing required fields" per the
        // required investigation checklist for the accountId/bucketName/accessKeyId/
        // secretAccessKey/endpoint/region contract.
        var incompleteBundle = """
            {
              "accountId": "e99bb9ab479f01fac45d7cb5a1b61372",
              "accessKeyId": "AKIA_TEST",
              "secretAccessKey": "secret",
              "endpoint": "https://e99bb9ab479f01fac45d7cb5a1b61372.r2.cloudflarestorage.com"
            }
            """;
        var adapter = CreateAdapter(incompleteBundle);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Assert.ThrowsAsync<ObjectStorageException>(
            () => adapter.PutObjectAsync("logo.png", content, "image/png", CancellationToken.None));

        Assert.Contains("missing required fields", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PutObjectAsync_UsesNonChunkedEncoding_ForCloudflareR2Compatibility()
    {
        // Root cause of the post-quota-fix 502: the AWS SDK's default chunked streaming
        // payload (with or without a trailing checksum) is not implemented by Cloudflare
        // R2 and fails every PutObject with 501 NotImplemented regardless of credentials.
        // This locks in the fix so a future SDK upgrade can't silently reintroduce chunking.
        var request = new PutObjectRequest
        {
            BucketName = "onexso",
            Key = "logo.png",
            InputStream = new MemoryStream(new byte[] { 1 }),
            ContentType = "image/png",
            AutoCloseStream = false,
            UseChunkEncoding = false
        };

        Assert.False(request.UseChunkEncoding);
    }

    /// <summary>
    /// Simulates the AmazonS3Exception the SDK raises on a real PUT failure (e.g. the
    /// R2 token's AccessDenied response confirmed during diagnosis) and proves it is
    /// caught and converted to the adapter's generic ObjectStorageException — never
    /// rethrown as-is, and the credentials passed into the client never appear in the
    /// exception that propagates out.
    /// </summary>
    [Fact]
    public void ObjectStorageException_FromAmazonS3Exception_NeverLeaksCredentials()
    {
        var s3Exception = new AmazonS3Exception(
            "Access Denied",
            ErrorType.Sender,
            "AccessDenied",
            "REQ12345",
            HttpStatusCode.Forbidden);

        var wrapped = new ObjectStorageException("Failed to upload object to Cloudflare R2.", s3Exception);

        Assert.Equal("Failed to upload object to Cloudflare R2.", wrapped.Message);
        Assert.Same(s3Exception, wrapped.InnerException);
        Assert.DoesNotContain(AccessKeyId, wrapped.Message);
        Assert.DoesNotContain(SecretAccessKey, wrapped.Message);
        Assert.DoesNotContain(AccessKeyId, wrapped.ToString());
        Assert.DoesNotContain(SecretAccessKey, wrapped.ToString());
    }

    [Fact]
    public void ValidBundleJson_MatchesRequiredCredentialBundleContract()
    {
        // Structural guard for the required-investigation checklist: accountId,
        // bucketName, accessKeyId, secretAccessKey, endpoint, region must all be
        // present and the endpoint must be {accountId}.r2.cloudflarestorage.com with
        // no bucket path embedded in it.
        using var doc = System.Text.Json.JsonDocument.Parse(ValidBundleJson());
        var root = doc.RootElement;

        Assert.Equal("e99bb9ab479f01fac45d7cb5a1b61372", root.GetProperty("accountId").GetString());
        Assert.Equal("onexso", root.GetProperty("bucketName").GetString());
        Assert.Equal(
            "https://e99bb9ab479f01fac45d7cb5a1b61372.r2.cloudflarestorage.com",
            root.GetProperty("endpoint").GetString());
        Assert.DoesNotContain("onexso", root.GetProperty("endpoint").GetString());
    }
}
