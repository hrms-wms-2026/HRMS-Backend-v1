using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Admin.Support;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Announcements;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Support;

/// <summary>
/// Developer Platform - platform announcements shown as a banner in the tenant app and/or the admin console.
/// Managing them reuses platform.support.read / platform.support.manage.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminAnnouncementsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminAnnouncementsController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("admin/v1/support/announcements")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "is_published")] bool? isPublished,
        [FromQuery(Name = "severity")] string? severity,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        CancellationToken ct)
        => ToResponse(await _mediator.Send(new ListPlatformAnnouncementsQuery(isPublished, severity, page, pageSize), ct));

    [HttpPost("admin/v1/support/announcements")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformAnnouncementRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        return ToResponse(await _mediator.Send(new CreatePlatformAnnouncementCommand(
            request.Title, request.Body, request.Severity, request.Audience, actorId), ct));
    }

    [HttpPatch("admin/v1/support/announcements/{id:guid}/publish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public Task<IActionResult> Publish(Guid id, CancellationToken ct) => SetPublished(id, true, ct);

    [HttpPatch("admin/v1/support/announcements/{id:guid}/unpublish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SupportManage)]
    public Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => SetPublished(id, false, ct);

    /// <summary>Banner feed for every signed-in platform user; no support permission needed to read it.</summary>
    [HttpGet("admin/v1/announcements/active")]
    public async Task<IActionResult> Active(CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetActiveAnnouncementsQuery(PlatformAnnouncement.AudiencePlatformAdmins), ct));

    private async Task<IActionResult> SetPublished(Guid id, bool publish, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } actorId) return Forbid();
        return ToResponse(await _mediator.Send(new SetPlatformAnnouncementPublishedCommand(id, publish, actorId), ct));
    }

    private IActionResult ToResponse<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
