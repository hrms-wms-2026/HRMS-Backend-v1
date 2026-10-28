using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class UploadObjectiveAssetFormRequest
{
    public IFormFile File { get; set; } = null!;
}
