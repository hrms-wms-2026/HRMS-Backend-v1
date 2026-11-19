using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.CreatePlatformFeatureFlag;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Commands.UpdatePlatformFeatureFlagStatus;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.Queries.ListPlatformFeatureFlags;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

[ApiController]
[Route("admin/v1/system-config/feature-flags")]
[Authorize(Policy = "AdminPolicy")]
public sealed class PlatformFeatureFlagsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformFeatureFlagsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.RuntimeFlagsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var response = await _mediator.Send(new ListPlatformFeatureFlagsQuery(), ct);
        return Ok(response);
    }

    [HttpPost]
    [RequirePlatformPermission(PlatformPermissionCatalog.RuntimeFlagsManage)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformFeatureFlagRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreatePlatformFeatureFlagCommand(request.Key, request.Name, request.Description, request.IsEnabled), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{id:guid}/status")]
    [RequirePlatformPermission(PlatformPermissionCatalog.RuntimeFlagsManage)]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdatePlatformFeatureFlagStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdatePlatformFeatureFlagStatusCommand(id, request.IsEnabled), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
