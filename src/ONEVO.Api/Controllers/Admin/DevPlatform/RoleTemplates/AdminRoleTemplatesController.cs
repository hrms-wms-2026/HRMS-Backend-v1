using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListRoleTemplates;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.RoleTemplates;

[ApiController]
[Route("admin/v1/role-templates")]
public sealed class AdminRoleTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminRoleTemplatesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListRoleTemplatesQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Create([FromBody] CreateRoleTemplateRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateRoleTemplateCommand(
                request.Name,
                request.Description,
                request.ModuleKeys,
                request.PermissionCodes),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{templateId:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Update(
        Guid templateId,
        [FromBody] UpdateRoleTemplateRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateRoleTemplateCommand(
                templateId,
                request.Name,
                request.Description,
                request.ModuleKeys,
                request.PermissionCodes,
                request.IsActive),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
