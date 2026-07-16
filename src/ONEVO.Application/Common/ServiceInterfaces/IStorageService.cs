namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string filePath, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
    string GetPublicUrl(string filePath);
}
