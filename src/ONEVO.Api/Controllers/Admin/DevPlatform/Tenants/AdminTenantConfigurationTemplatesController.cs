using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListTenantConfigurationTemplateApplications;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminTenantConfigurationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminTenantConfigurationTemplatesController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost("admin/v1/tenants/{tenantId:guid}/configuration-templates/{templateId:guid}/apply")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Apply(
        Guid tenantId,
        Guid templateId,
        [FromBody] ApplyConfigurationTemplateRequest? body,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var request = body ?? new ApplyConfigurationTemplateRequest(false);
        var result = await _mediator.Send(
            new ApplyConfigurationTemplateToTenantCommand(tenantId, templateId, request.ForceUpdate, actorId.Value),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("admin/v1/tenants/{tenantId:guid}/configuration-template-applications")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsRead)]
    public async Task<IActionResult> ListApplications(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListTenantConfigurationTemplateApplicationsQuery(tenantId, page, pageSize),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
