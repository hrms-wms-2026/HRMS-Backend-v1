using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Storage;
using ONEVO.Application.Features.Storage.File.Commands.DeleteFile;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.Storage.File.Queries.GetFile;

namespace ONEVO.Api.Controllers.Tenant.Storage;

[ApiController]
[Route("api/v1/files")]
[Authorize(Policy = "TenantPolicy")]
public sealed class FilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FilesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadFileFormRequest request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        var result = await _mediator.Send(
            new UploadFileCommand(request.Purpose, request.File.FileName, request.File.ContentType, stream), ct);

        return result.IsSuccess
            ? StatusCode(201, new UploadFileViewModel(
                result.Value!.Id,
                result.Value.OriginalFileName,
                result.Value.FileSizeBytes,
                result.Value.ContentType))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteFileCommand(fileId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{fileId:guid}")]
    public async Task<IActionResult> Get(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetFileQuery(fileId), ct);
        return result.IsSuccess
            ? File(result.Value!.Content, result.Value.ContentType)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
