using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Application.Features.Auth.Legal.Commands.AcceptPendingLegalDocuments;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Api.Filters;

namespace ONEVO.Api.Controllers.Tenant.Auth;

[NoResponseCompression]
[ApiController]
public class AuthPendingLegalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthPendingLegalController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>Return pending required documents while a legal acceptance challenge is active.</summary>
    [HttpPost("/api/v1/legal/acceptances/complete-login")]
    // Public by design: needs the crownus_legal_pending challenge cookie + matching CSRF token; AuthRateLimiting per IP.
    [AllowAnonymous]
    public async Task<IActionResult> AcceptPendingLegalDocuments(
        [FromBody] AcceptPendingLegalDocumentsRequest request,
        CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("crownus_legal_pending", out var legalChallenge)
            || string.IsNullOrWhiteSpace(legalChallenge))
        {
            return Problem("Legal acceptance session is missing or expired.", statusCode: 401);
        }

        var csrfToken = Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(csrfToken))
        {
            return Problem("A valid CSRF token is required for this request.", statusCode: 403);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var items = request.Acceptances
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision, x.ContentHash))
            .ToList();

        var result = await _mediator.Send(
            new AcceptPendingLegalDocumentsCommand(legalChallenge, csrfToken, items, ip, ua), ct);

        if (result.IsSuccess && result.Value?.RequiresLegalAcceptance == false)
        {
            this.DeleteTenantCookie(
                "crownus_legal_pending", httpOnly: true, _env, path: "/api/v1/legal/acceptances/complete-login");
            this.DeleteTenantCookie("crownus_legal_csrf", httpOnly: false, _env);
        }

        return await this.HandleSessionResultAsync(result, _env);
    }
}
