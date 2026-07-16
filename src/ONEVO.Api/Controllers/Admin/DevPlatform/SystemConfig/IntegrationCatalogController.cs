using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.CreateIntegration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.LinkIntegrationModule;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.SetIntegrationActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UnlinkIntegrationModule;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UpdateIntegration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Queries.GetIntegration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Queries.ListIntegrations;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

[ApiController]
[Authorize(Policy = "AdminPolicy")]
[Route("admin/v1/system-config/integrations")]
public sealed class IntegrationCatalogController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentPlatformUserContext _currentUser;

    public IntegrationCatalogController(
        IMediator mediator,
        ICurrentPlatformUserContext currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListIntegrationsQuery(), ct);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{integrationKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]
    public async Task<IActionResult> Get(string integrationKey, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetIntegrationQuery(integrationKey),
            ct);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateIntegrationCatalogRequest request,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        if (request.IsActive is null)
        {
            return Problem("isActive is required.", statusCode: 400);
        }

        var command = new CreateIntegrationCommand(
            request.IntegrationKey,
            request.DisplayName,
            request.Description,
            request.ConnectionScope,
            request.OnevoAppProvider,
            request.LogoUrl,
            request.IsActive.Value,
            actorId.Value);

        var result = await _mediator.Send(command, ct);
        if (result.IsSuccess)
        {
            return Created(
                $"admin/v1/system-config/integrations/{result.Value!.IntegrationKey}",
                result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{integrationKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Update(
        string integrationKey,
        [FromBody] UpdateIntegrationCatalogRequest request,
        CancellationToken ct)
    {
        var command = new UpdateIntegrationCommand(
            integrationKey,
            request.DisplayName,
            request.Description,
            request.ConnectionScope,
            request.OnevoAppProvider,
            request.LogoUrl,
            request.IsActive);

        var result = await _mediator.Send(command, ct);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{integrationKey}/activate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public Task<IActionResult> Activate(string integrationKey, CancellationToken ct)
    {
        return SetActivation(integrationKey, true, ct);
    }

    [HttpPost("{integrationKey}/deactivate")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public Task<IActionResult> Deactivate(string integrationKey, CancellationToken ct)
    {
        return SetActivation(integrationKey, false, ct);
    }

    [HttpPost("{integrationKey}/modules/{moduleKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Link(
        string integrationKey,
        string moduleKey,
        CancellationToken ct)
    {
        var actorId = _currentUser.UserId;
        if (actorId is null)
        {
            return Forbid();
        }

        var command = new LinkIntegrationModuleCommand(
            integrationKey,
            moduleKey,
            actorId.Value);

        var result = await _mediator.Send(command, ct);
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{integrationKey}/modules/{moduleKey}")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigManage)]
    public async Task<IActionResult> Unlink(
        string integrationKey,
        string moduleKey,
        CancellationToken ct)
    {
        var command = new UnlinkIntegrationModuleCommand(integrationKey, moduleKey);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private async Task<IActionResult> SetActivation(
        string integrationKey,
        bool isActive,
        CancellationToken ct)
    {
        var command = new SetIntegrationActivationCommand(integrationKey, isActive);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
