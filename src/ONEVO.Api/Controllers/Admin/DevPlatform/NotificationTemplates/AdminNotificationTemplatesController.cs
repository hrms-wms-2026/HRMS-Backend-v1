using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.PublishNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.RestoreNotificationTemplateVersion;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.GetNotificationTemplateDetail;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationTemplates;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.PreviewNotificationTemplate;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.NotificationTemplates;

/// <summary>Platform-admin management of global notification wording. Drafts never affect live sends;
/// publishing switches the active version for new sends.</summary>
[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminNotificationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminNotificationTemplatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("admin/v1/notification-templates")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "event_key")] string? eventKey, [FromQuery] string? search, CancellationToken ct)
        => ToResponse(await _mediator.Send(new ListNotificationTemplatesQuery(eventKey, search), ct));

    [HttpGet("admin/v1/notification-templates/{templateId:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> Get(Guid templateId, CancellationToken ct)
        => ToResponse(await _mediator.Send(new GetNotificationTemplateDetailQuery(templateId), ct));

    [HttpGet("admin/v1/notification-templates/{templateId:guid}/versions")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> Versions(Guid templateId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNotificationTemplateDetailQuery(templateId), ct);
        return result.IsSuccess ? Ok(result.Value!.Versions) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/notification-templates")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationTemplateRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateNotificationTemplateCommand(request.EventKey, request.Name, request.Content), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { templateId = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("admin/v1/notification-templates/{templateId:guid}/draft")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> SaveDraft(Guid templateId, [FromBody] SaveNotificationTemplateDraftRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new SaveNotificationTemplateDraftCommand(templateId, request.Content, request.Revision), ct));

    [HttpPost("admin/v1/notification-templates/{templateId:guid}/preview")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> Preview(Guid templateId, [FromBody] PreviewNotificationTemplateRequest? request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new PreviewNotificationTemplateQuery(templateId, request?.Content, request?.Variables), ct));

    [HttpPost("admin/v1/notification-templates/{templateId:guid}/publish")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Publish(Guid templateId, [FromBody] PublishNotificationTemplateRequest request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new PublishNotificationTemplateCommand(templateId, request.Revision), ct));

    [HttpPost("admin/v1/notification-templates/{templateId:guid}/versions/{versionId:guid}/restore")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Restore(Guid templateId, Guid versionId, CancellationToken ct)
        => ToResponse(await _mediator.Send(new RestoreNotificationTemplateVersionCommand(templateId, versionId), ct));

    [HttpPost("admin/v1/notification-templates/{templateId:guid}/test")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> SendTest(Guid templateId, [FromBody] SendTestNotificationRequest? request, CancellationToken ct)
        => ToResponse(await _mediator.Send(new SendTestNotificationCommand(
            templateId, request?.Channel ?? NotificationChannelNames.Email, request?.Variables), ct));

    private IActionResult ToResponse<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
