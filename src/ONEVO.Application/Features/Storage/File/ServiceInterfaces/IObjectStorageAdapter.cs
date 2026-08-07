namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

public interface IObjectStorageAdapter
{
    Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default);

    Task DeleteObjectAsync(string objectKey, CancellationToken ct = default);

    Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default);

    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Returns an expiring pre-signed URL granting temporary GET access.
    /// Never expose permanent URLs for evidence or HR files — always use an expiring URL.
    /// </summary>
    Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default);
}
