namespace ONEVO.Api.Contracts.Storage;

public sealed class UploadFileFormRequest
{
    public string Purpose { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
