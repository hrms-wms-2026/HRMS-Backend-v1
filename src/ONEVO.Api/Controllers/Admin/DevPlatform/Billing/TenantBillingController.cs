using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Billing;

[ApiController]
[Route("admin/v1/tenants/{tenantId:guid}/billing")]
public class TenantBillingController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantBillingController(IMediator mediator) => _mediator = mediator;

    [HttpGet("one-time-charges")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsRead)]
    public async Task<IActionResult> GetOneTimeCharges(Guid tenantId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTenantOneTimeChargesQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("one-time-charges")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> CreateOneTimeCharge(
        Guid tenantId,
        [FromBody] CreateOneTimeChargeRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateOneTimeChargeCommand(tenantId, request.SetupOptionKey, request.Description, request.Amount, request.Currency),
            ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetOneTimeCharges), new { tenantId }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("one-time-charges/{chargeId:guid}")]
    [Authorize(Policy = "AdminPolicy")]
    [RequirePlatformPermission(PlatformPermissionCatalog.SubscriptionsManage)]
    public async Task<IActionResult> UpdateOneTimeCharge(
        Guid tenantId,
        Guid chargeId,
        [FromBody] UpdateOneTimeChargeRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateOneTimeChargeCommand(tenantId, chargeId, request.Amount, request.Status),
            ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
