namespace ONEVO.Application.Features.Storage.File.ServiceInterfaces;

public interface IObjectStorageAdapter
{
    Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default);

    Task DeleteObjectAsync(string objectKey, CancellationToken ct = default);

    Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default);

    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);
}
