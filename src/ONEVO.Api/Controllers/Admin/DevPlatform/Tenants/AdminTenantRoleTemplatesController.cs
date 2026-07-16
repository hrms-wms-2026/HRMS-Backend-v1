using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Tenants;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/role-templates")]
public sealed class AdminTenantRoleTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminTenantRoleTemplatesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a tenant role from a platform template or refreshes permissions when forceUpdate is true.</summary>
    [HttpPost("{templateId:guid}/apply")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TenantsManage)]
    public async Task<IActionResult> Apply(
        Guid tenantId,
        Guid templateId,
        [FromBody] ApplyRoleTemplateRequest? body,
        CancellationToken ct)
    {
        var req = body ?? new ApplyRoleTemplateRequest(null, false);
        var result = await _mediator.Send(
            new ApplyRoleTemplateCommand(tenantId, templateId, req.RoleNameOverride, req.ForceUpdate),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
