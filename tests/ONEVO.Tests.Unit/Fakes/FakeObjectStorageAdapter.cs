using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Storage;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeObjectStorageAdapter : IObjectStorageAdapter
{
    public bool ShouldFailPut { get; set; }
    public List<string> PutObjectKeys { get; } = new();
    public List<string> DeletedObjectKeys { get; } = new();

    public Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        if (ShouldFailPut)
        {
            throw new ObjectStorageException("simulated R2 upload failure");
        }

        PutObjectKeys.Add(objectKey);
        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        DeletedObjectKeys.Add(objectKey);
        return Task.CompletedTask;
    }

    public Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default)
    {
        return Task.FromResult(PutObjectKeys.Contains(objectKey));
    }
}
