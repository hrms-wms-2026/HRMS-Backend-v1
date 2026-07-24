using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.DeactivateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.GetConfigurationTemplateDetail;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListConfigurationTemplates;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.ConfigurationTemplates;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
public sealed class AdminConfigurationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public AdminConfigurationTemplatesController(IMediator mediator, ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("admin/v1/configuration-templates")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "type")] string? templateType,
        [FromQuery(Name = "active_only")] bool? activeOnly,
        [FromQuery(Name = "industry_tag")] string? industryTag,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListConfigurationTemplatesQuery(templateType, activeOnly, industryTag, page, pageSize),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("admin/v1/configuration-templates/{templateId:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesRead)]
    public async Task<IActionResult> GetById(Guid templateId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetConfigurationTemplateDetailQuery(templateId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/configuration-templates")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Create([FromBody] CreateConfigurationTemplateRequest request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(
            new CreateConfigurationTemplateCommand(
                request.TemplateKey,
                request.TemplateType,
                request.Name,
                request.Description,
                request.ModuleKeys,
                request.IndustryProfileTag,
                request.PayloadJson,
                request.IsSystem,
                actorId.Value),
            ct);
        return result.IsSuccess
            ? Created($"admin/v1/configuration-templates/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("admin/v1/configuration-templates/{templateId:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Update(
        Guid templateId,
        [FromBody] UpdateConfigurationTemplateRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateConfigurationTemplateMetadataCommand(
                templateId,
                request.Name,
                request.Description,
                request.ModuleKeys,
                request.IndustryProfileTag,
                request.PayloadJson),
            ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("admin/v1/configuration-templates/{templateId:guid}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Deactivate(Guid templateId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeactivateConfigurationTemplateCommand(templateId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("admin/v1/configuration-templates/{templateId:guid}/clone")]
    [RequirePlatformPermission(PlatformPermissionCatalog.TemplatesManage)]
    public async Task<IActionResult> Clone(Guid templateId, CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await _mediator.Send(new CloneConfigurationTemplateCommand(templateId, actorId.Value), ct);
        return result.IsSuccess
            ? Created($"admin/v1/configuration-templates/{result.Value!.Id}", result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
