namespace ONEVO.Api.Contracts.Storage;

public sealed record UploadFileViewModel(
    Guid FileId,
    string OriginalFileName,
    long FileSizeBytes,
    string ContentType);
