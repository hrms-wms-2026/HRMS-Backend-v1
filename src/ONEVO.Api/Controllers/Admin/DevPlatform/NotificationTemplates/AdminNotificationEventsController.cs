using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationEvents;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.NotificationTemplates;

/// <summary>The code-registered notification events templates can be written for (read-only).</summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminNotificationEventsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationEventsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("admin/v1/notification-events")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> List([FromQuery] string? module, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListNotificationEventsQuery(module), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
