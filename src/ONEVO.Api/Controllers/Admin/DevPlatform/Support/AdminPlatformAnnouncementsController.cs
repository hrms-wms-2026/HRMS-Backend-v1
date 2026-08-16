using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreatePlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.Commands.PublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UnpublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Support.Queries.ListPlatformAnnouncements;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Support;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminPlatformAnnouncementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminPlatformAnnouncementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("admin/v1/support/announcements")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "is_published")] bool? isPublished,
        [FromQuery] string? severity,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPlatformAnnouncementsQuery(isPublished, severity, page, pageSize), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/support/announcements")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformAnnouncementRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreatePlatformAnnouncementCommand(request.Title, request.Body, request.Severity, request.Audience),
            ct);
        return result.IsSuccess
            ? Created($"admin/v1/support/announcements/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("admin/v1/support/announcements/{announcementId:guid}/publish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Publish(Guid announcementId, CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishPlatformAnnouncementCommand(announcementId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("admin/v1/support/announcements/{announcementId:guid}/unpublish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Unpublish(Guid announcementId, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnpublishPlatformAnnouncementCommand(announcementId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
