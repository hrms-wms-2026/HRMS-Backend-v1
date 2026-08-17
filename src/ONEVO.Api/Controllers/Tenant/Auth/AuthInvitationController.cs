using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptEmployeeInvitation;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationGoogle;
using ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;
using ONEVO.Application.Features.Auth.Invite.Queries.GetInvitationByToken;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthInvitationController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthInvitationController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>Public invitation preview (token from email link).</summary>
    [HttpGet("invitations/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInvitation(string token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetInvitationByTokenQuery(token), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 404);
        return Ok(result.Value);
    }

    /// <summary>Complete invitation by setting a password and accepting required legal documents.</summary>
    [HttpPost("invitations/{token}/accept-password")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvitationPassword(
        string token,
        [FromBody] AcceptInvitationPasswordRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var acceptances = request.Acceptances
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision))
            .ToList();
        var result = await _mediator.Send(
            new AcceptInvitationPasswordCommand(
                token,
                request.Password,
                request.ConfirmPassword,
                acceptances,
                ip,
                ua),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return await this.HandleSessionResultAsync(result, _env);
    }

    /// <summary>
    /// Complete an employee onboarding invitation by setting a password and accepting required
    /// legal documents. Must run against the tenant subdomain the invite was issued for (the
    /// invite link points there) - ITenantContext must resolve to inv.TenantId or the command
    /// returns 404, mirroring AcceptInvitationPassword's own tenant-host check.
    /// </summary>
    [HttpPost("invitations/{token}/accept-employee")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptEmployeeInvitation(
        string token,
        [FromBody] AcceptEmployeeInvitationRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var acceptances = request.Acceptances
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision))
            .ToList();
        var result = await _mediator.Send(
            new AcceptEmployeeInvitationCommand(
                token,
                request.Password,
                request.ConfirmPassword,
                acceptances,
                ip,
                ua),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return await this.HandleSessionResultAsync(result, _env);
    }

    /// <summary>Complete invitation with Google Sign-In.</summary>
    [HttpPost("invitations/{token}/accept-google")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvitationGoogle(
        string token,
        [FromBody] AcceptInvitationGoogleRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var acceptances = request.Acceptances
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision))
            .ToList();
        var result = await _mediator.Send(
            new AcceptInvitationGoogleCommand(token, request.GoogleIdToken, acceptances, ip, ua),
            ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return await this.HandleSessionResultAsync(result, _env);
    }
}
