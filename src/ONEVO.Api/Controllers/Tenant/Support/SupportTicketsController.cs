using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Support;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;

namespace ONEVO.Api.Controllers.Tenant.Support;

/// <summary>
/// Tickets the tenant raises with Crownus support. Always scoped to the caller's tenant; no tenant id is
/// accepted from the client, and internal support notes are never returned.
/// </summary>
[ApiController]
[Route("api/v1/support/tickets")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission(PermissionCatalog.Support.Manage)]
public sealed class SupportTicketsController : ControllerBase
{
    private const long MaxUploadRequestBytes = 26 * 1024 * 1024;

    private readonly IMediator _mediator;

    public SupportTicketsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        CancellationToken ct)
        => ToResponse(await _mediator.Send(new ListTenantSupportTicketsQuery(status, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetTenantSupportTicketQuery(id), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantSupportTicketRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new CreateTenantSupportTicketCommand(
            request.Subject, request.Description, request.Priority, request.Category, request.CorrelationIds), ct));

    /// <summary>Edit while the ticket is open or in progress. Same body as create; correlation_ids is ignored
    /// (references are attached only when the ticket is raised).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateTenantSupportTicketRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new UpdateTenantSupportTicketCommand(
            id, request.Subject, request.Description, request.Priority, request.Category), ct));

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> AddComment(
        Guid id, [FromBody] AddTenantSupportTicketCommentRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new AddTenantSupportTicketCommentCommand(id, request.Body), ct));

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    public async Task<IActionResult> UploadAttachment(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Problem("A file is required.", statusCode: 400);

        await using var stream = file.OpenReadStream();
        return ToResponse(await _mediator.Send(new UploadTenantSupportTicketAttachmentCommand(
            id, new SupportUpload(stream, file.FileName, file.ContentType, file.Length)), ct));
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, Guid attachmentId, CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetTenantSupportAttachmentDownloadUrlQuery(id, attachmentId), ct));

    private IActionResult ToResponse<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
