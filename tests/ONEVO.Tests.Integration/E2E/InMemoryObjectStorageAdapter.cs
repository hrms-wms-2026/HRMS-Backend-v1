using System.Collections.Concurrent;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Tests.Integration.E2E;

/// <summary>
/// In-memory stand-in for Cloudflare R2, registered by E2ETestFactory in place
/// of the real CloudflareR2ObjectStorageAdapter. No integration test in this
/// codebase has real R2 credentials available, so without this the real
/// UploadAsync/OpenReadAsync path is entirely untestable end-to-end. Actually
/// stores bytes (not just a placeholder stream) so round-trip upload/read
/// assertions are meaningful.
/// </summary>
public sealed class InMemoryObjectStorageAdapter : IObjectStorageAdapter
{
    private sealed record StoredObject(byte[] Bytes, string ContentType);

    private readonly ConcurrentDictionary<string, StoredObject> _objects = new();

    public Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        content.Position = 0;
        content.CopyTo(buffer);
        _objects[objectKey] = new StoredObject(buffer.ToArray(), contentType);
        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        _objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }

    public Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        if (!_objects.TryGetValue(objectKey, out var stored))
            throw new ONEVO.Infrastructure.ExternalServices.Storage.ObjectStorageException(
                $"Object '{objectKey}' not found.");

        return Task.FromResult<Stream>(new MemoryStream(stored.Bytes));
    }

    public Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default)
        => Task.FromResult(_objects.ContainsKey(objectKey));

    public Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult($"https://in-memory-storage.test/{objectKey}?expires={DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds()}");
}
