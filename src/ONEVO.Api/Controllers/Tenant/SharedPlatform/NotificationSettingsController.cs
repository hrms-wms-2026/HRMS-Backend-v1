using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Queries.GetNotificationSettings;

namespace ONEVO.Api.Controllers.Tenant.SharedPlatform;

/// <summary>Tenant routing of notification events to in-app, email and tray. Always scoped to the
/// caller's tenant; no tenant id is accepted from the client.</summary>
[ApiController]
[Route("api/v1/settings/notifications")]
[Authorize(Policy = "TenantPolicy")]
[RequirePermission("notifications:manage")]
public sealed class NotificationSettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetNotificationSettingsQuery(), ct));

    [HttpPut("{eventKey}")]
    public async Task<IActionResult> Update(string eventKey, [FromBody] UpdateNotificationSettingRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new UpdateNotificationSettingCommand(
            eventKey, request.EnabledChannels ?? [], request.TemplateId, request.RecipientRule, request.Revision), ct));

    [HttpDelete("{eventKey}")]
    public async Task<IActionResult> Reset(string eventKey, CancellationToken ct)
        => ToResponse(await _mediator.Send(new ResetNotificationSettingCommand(eventKey), ct));

    private IActionResult ToResponse<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
