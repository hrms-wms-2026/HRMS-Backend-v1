using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnGitHubConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.SetGitHubTenantApproval;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Requests;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetGitHubUserIntegrationStatus;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetGitHubTenantApproval;

namespace ONEVO.Api.Controllers.Tenant.Integrations;

[ApiController]
[Route("api/v1/integrations/github")]
[Authorize(Policy = "TenantPolicy")]
public sealed class GitHubIntegrationController : ControllerBase
{
    private readonly IMediator _mediator;

    public GitHubIntegrationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("connect/start")]
    public async Task<IActionResult> Start(
        [FromBody] StartGitHubOAuthRequest request,
        CancellationToken ct)
    {
        var redirectUri = Url.ActionLink(nameof(Callback), values: null, protocol: Request.Scheme);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem("GitHub callback URL could not be generated.", statusCode: 500);
        }

        var result = await _mediator.Send(
            new StartGitHubUserOAuthCommand(request.ReturnUrl, redirectUri),
            ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpGet("connect/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        var redirectUri = Url.ActionLink(nameof(Callback), values: null, protocol: Request.Scheme);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem("GitHub callback URL could not be generated.", statusCode: 500);
        }

        var result = await _mediator.Send(
            new CompleteGitHubUserOAuthCommand(code, state, redirectUri),
            ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetGitHubUserIntegrationStatusQuery(), ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var result = await _mediator.Send(
            new DisconnectOwnUserIntegrationCommand(GitHubUserOAuthRules.IntegrationKey),
            ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshOwnGitHubConnectionCommand(), ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpGet("tenant/status")]
    [RequirePermission("integrations:manage")]
    public async Task<IActionResult> TenantStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetGitHubTenantApprovalQuery(), ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpPost("tenant/enable")]
    [RequirePermission("integrations:manage")]
    public async Task<IActionResult> EnableTenant(CancellationToken ct)
    {
        var result = await _mediator.Send(new SetGitHubTenantApprovalCommand(true), ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }

    [HttpPost("tenant/disable")]
    [RequirePermission("integrations:manage")]
    public async Task<IActionResult> DisableTenant(CancellationToken ct)
    {
        var result = await _mediator.Send(new SetGitHubTenantApprovalCommand(false), ct);
        if (!result.IsSuccess)
        {
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        }

        return Ok(result.Value);
    }
}
