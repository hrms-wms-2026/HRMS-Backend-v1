using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.Services;

namespace ONEVO.Api.Controllers.Tenant.Legal;

[ApiController]
[Route("api/v1/legal")]
[Authorize(Policy = "TenantPolicy")]
public class LegalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILegalAcceptanceChecker _checker;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public LegalController(
        IMediator mediator,
        ILegalAcceptanceChecker checker,
        ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        _mediator = mediator;
        _checker = checker;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
    }

    /// <summary>Get pending legal documents for the currently authenticated tenant user.</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingLegalDocuments(CancellationToken ct)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Problem("Tenant context is not resolved.", statusCode: 400);

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
            return Problem("User identity is not authenticated.", statusCode: 401);

        var result = await _checker.CheckAsync(_tenantContext.TenantId, userId, ct);
        return Ok(result);
    }

    /// <summary>Submit legal acceptance decisions for the currently authenticated tenant user.</summary>
    [HttpPost("acceptances")]
    public async Task<IActionResult> SubmitAcceptances(
        [FromBody] SubmitAcceptancesRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var items = request.Acceptances?
            .Select(x => new LegalAcceptanceItemInput(x.DocumentType, x.Version, x.Decision, x.ContentHash))
            .ToList() ?? [];

        var command = new SubmitLegalAcceptanceCommand(items, ip, ua);
        var result = await _mediator.Send(command, ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return Ok(new { success = true });
    }

    public record AcceptanceItemRequest(
        [property: JsonPropertyName("document_type")] string DocumentType,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("content_hash")] string? ContentHash = null
    );

    public record SubmitAcceptancesRequest(
        [property: JsonPropertyName("acceptances")] IReadOnlyList<AcceptanceItemRequest> Acceptances
    );
}
