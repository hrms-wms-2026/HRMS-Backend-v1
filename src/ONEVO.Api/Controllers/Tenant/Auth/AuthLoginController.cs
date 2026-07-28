using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;
using ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;
using ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthLoginController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenantContext;

    public AuthLoginController(IMediator mediator, IWebHostEnvironment env, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _env = env;
        _tenantContext = tenantContext;
    }

    /// <summary>Base-host credential-first email + password login only. Tenant-host password login is not supported.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        this.ClearInvalidTenantSessionCookies(_env);

        if (_tenantContext.ContextMode == TenantContextMode.Tenant)
            return Problem("Tenant-host password login is not supported.", statusCode: 400);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var baseResult = await _mediator.Send(new BaseLoginCommand(request.Email, request.Password, ip, ua), ct);

        if (!baseResult.IsSuccess)
            return Problem(baseResult.Error, statusCode: baseResult.StatusCode ?? 400);

        var baseDto = baseResult.Value!;
        if (baseDto.WorkspaceSelection is not null)
        {
            var selection = baseDto.WorkspaceSelection;
            var workspaceOptions = selection.Workspaces
                .Select(w => new WorkspaceOptionResponse(w.Slug, w.DisplayName))
                .ToList();
            var response = new WorkspaceSelectionRequiredResponse(true, selection.LoginChallenge, workspaceOptions);
            return StatusCode(202, response);
        }

        var sessionResult = Result<LoginResponseDto>.Success(baseDto.Session!);
        return await this.HandleSessionResultAsync(sessionResult, _env);
    }

    /// <summary>Complete base-domain login after multiple candidate workspaces were offered.</summary>
    [HttpPost("login/select-workspace")]
    [AllowAnonymous]
    public async Task<IActionResult> SelectWorkspace([FromBody] SelectWorkspaceRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new SelectWorkspaceCommand(request.LoginChallenge, request.Workspace, ip, ua), ct);

        return await this.HandleSessionResultAsync(result, _env);
    }

    /// <summary>Base-domain Google login: verify Google identity first, then resolve eligible workspace(s).</summary>
    [HttpPost("login/google")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginWithGoogle([FromBody] BaseGoogleLoginRequest request, CancellationToken ct)
    {
        if (_tenantContext.ContextMode == TenantContextMode.Tenant)
            return Problem("Google sign-in is not available on this host.", statusCode: 400);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var baseResult = await _mediator.Send(new BaseGoogleLoginCommand(request.GoogleIdToken, ip, ua), ct);

        if (!baseResult.IsSuccess)
            return Problem(baseResult.Error, statusCode: baseResult.StatusCode ?? 400);

        var baseDto = baseResult.Value!;
        if (baseDto.WorkspaceSelection is not null)
        {
            var selection = baseDto.WorkspaceSelection;
            var workspaceOptions = selection.Workspaces
                .Select(w => new WorkspaceOptionResponse(w.Slug, w.DisplayName))
                .ToList();
            var response = new WorkspaceSelectionRequiredResponse(true, selection.LoginChallenge, workspaceOptions);
            return StatusCode(202, response);
        }

        var sessionResult = Result<LoginResponseDto>.Success(baseDto.Session!);
        return await this.HandleSessionResultAsync(sessionResult, _env);
    }
}
