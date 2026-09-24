using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public class TaskPendingUploadFormRequest
{
    public string Purpose { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
