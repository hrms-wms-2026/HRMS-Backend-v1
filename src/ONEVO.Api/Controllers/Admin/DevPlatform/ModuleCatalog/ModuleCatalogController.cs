using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.ModuleCatalog;

[ApiController]
[Route("admin/v1/modules/catalog")]
[Authorize(Policy = "AdminPolicy")]
[RequirePlatformPermission(PlatformPermissionCatalog.ModuleCatalogRead)]
[Produces("application/json")]
public class ModuleCatalogController : ControllerBase
{
    private readonly IMediator _mediator;

    public ModuleCatalogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleCatalogListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetModuleCatalogListQuery(), cancellationToken);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("{moduleKey}")]
    [ProducesResponseType(typeof(ModuleCatalogDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetail(string moduleKey, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetModuleCatalogDetailQuery(moduleKey), cancellationToken);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("{moduleKey}/features")]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleFeatureDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatures(string moduleKey, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetModuleCatalogFeaturesQuery(moduleKey), cancellationToken);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    [HttpGet("{moduleKey}/permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<ModulePermissionOwnershipDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(string moduleKey, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetModuleCatalogPermissionsQuery(moduleKey), cancellationToken);
        if (!result.IsSuccess) return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
