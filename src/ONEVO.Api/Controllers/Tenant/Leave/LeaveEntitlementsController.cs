using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Entitlements;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Entitlement.Commands.AdjustEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.Commands.CreateManualEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.Queries.ListEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.Queries.PreviewGenerateEntitlements;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/entitlements")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveEntitlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveEntitlementsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List(
        [FromQuery] int year,
        [FromQuery] Guid? legalEntityId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListEntitlementsQuery(year, legalEntityId, departmentId, leaveTypeId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("generate/preview")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> PreviewGenerate(
        [FromBody] GenerateEntitlementsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new PreviewGenerateEntitlementsQuery(request.Year, request.LegalEntityId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("generate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateEntitlementsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GenerateEntitlementsCommand(request.Year, request.LegalEntityId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("manual")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> CreateManual(
        [FromBody] CreateManualEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CreateManualEntitlementCommand(
            request.EmployeeId,
            request.LeaveTypeId,
            request.Year,
            request.TotalDays,
            request.CarriedForwardDays,
            request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{entitlementId:guid}/adjust")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Adjust(
        Guid entitlementId, [FromBody] AdjustEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new AdjustEntitlementCommand(
            entitlementId,
            request.TotalDays,
            request.CarriedForwardDays,
            request.Reason,
            request.ConfirmNegativeRemaining), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{entitlementId:guid}/recalculate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Recalculate(
        Guid entitlementId, [FromBody] RecalculateEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new RecalculateEntitlementCommand(entitlementId, request.ConfirmNegativeRemaining), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
