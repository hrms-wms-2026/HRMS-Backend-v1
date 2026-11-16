using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Admin.Support;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Admin;
using ONEVO.Application.Features.DevPlatform.Support.Services;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Support;

/// <summary>
/// Developer Platform - support tickets across every tenant. Tickets are tenant-owned rows; platform staff
/// reach them through admin context mode. Reuses platform.support.read / platform.support.manage.
/// </summary>
[ApiController]
[Route("admin/v1/support")]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminSupportTicketsController : ControllerBase
{
    private const long MaxUploadRequestBytes = 26 * 1024 * 1024;

    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminSupportTicketsController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    /// <param name="assignee">A platform user id, or "unassigned".</param>
    [HttpGet("tickets")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "priority")] string? priority,
        [FromQuery(Name = "tenant_id")] Guid? tenantId,
        [FromQuery(Name = "assignee")] string? assignee,
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery(Name = "awaiting_support")] bool? awaitingSupport,
        CancellationToken ct)
    {
        var unassigned = string.Equals(assignee, "unassigned", StringComparison.OrdinalIgnoreCase);
        Guid? assigneeId = null;
        if (!unassigned && !string.IsNullOrWhiteSpace(assignee))
        {
            if (!Guid.TryParse(assignee, out var parsed))
            {
                return Problem("assignee must be a platform user id or \"unassigned\".", statusCode: 400);
            }

            assigneeId = parsed;
        }

        return ToResponse(await _mediator.Send(
            new ListSupportTicketsQuery(
                status, priority, tenantId, assigneeId, unassigned, search, page, pageSize, awaitingSupport == true), ct));
    }

    /// <summary>Counts behind the support bell: tickets waiting on support, those assigned to the caller, open unassigned.</summary>
    [HttpGet("summary")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        return ToResponse(await _mediator.Send(new GetSupportSummaryQuery(actorId), ct));
    }

    [HttpGet("tickets/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetSupportTicketQuery(id), ct));

    [HttpPut("tickets/{id:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupportTicketRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new UpdateSupportTicketCommand(
            id, request.Subject, request.Description, request.Priority, request.Category), ct));

    [HttpPost("tickets")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        return ToResponse(await _mediator.Send(new CreateSupportTicketCommand(
            request.TenantId, request.Subject, request.Description, request.Priority, request.Category, actorId), ct));
    }

    [HttpPatch("tickets/{id:guid}/status")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateSupportTicketStatusRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new UpdateSupportTicketStatusCommand(id, request.Status), ct));

    [HttpPatch("tickets/{id:guid}/assignee")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignSupportTicketRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new AssignSupportTicketCommand(id, request.AssignedToPlatformUserId), ct));

    [HttpPost("tickets/{id:guid}/comments")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> AddComment(
        Guid id, [FromBody] AddSupportTicketCommentRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        return ToResponse(await _mediator.Send(
            new AddSupportTicketCommentCommand(id, request.Body, request.IsInternal, actorId), ct));
    }

    [HttpPost("tickets/{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> UploadAttachment(
        Guid id, IFormFile? file, [FromForm(Name = "is_internal")] bool isInternal, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        if (file is null || file.Length == 0) return Problem("A file is required.", statusCode: 400);

        await using var stream = file.OpenReadStream();
        return ToResponse(await _mediator.Send(new UploadSupportTicketAttachmentCommand(
            id, new SupportUpload(stream, file.FileName, file.ContentType, file.Length), isInternal, actorId), ct));
    }

    [HttpGet("tickets/{id:guid}/attachments/{attachmentId:guid}/download-url")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> GetDownloadUrl(Guid id, Guid attachmentId, CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetSupportAttachmentDownloadUrlQuery(id, attachmentId), ct));

    /// <summary>Active platform users a ticket can be assigned to.</summary>
    [HttpGet("assignees")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> ListAssignees(CancellationToken ct)
        => ToResponse(await _mediator.Send(new ListSupportAssigneesQuery(), ct));

    private IActionResult ToResponse<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
