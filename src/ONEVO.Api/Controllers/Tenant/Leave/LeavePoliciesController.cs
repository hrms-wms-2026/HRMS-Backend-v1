using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/policies")]
[Authorize(Policy = "TenantPolicy")]
public class LeavePoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeavePoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLeavePoliciesQuery(includeInactive), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{leavePolicyId:guid}")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Get(Guid leavePolicyId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeavePolicyQuery(leavePolicyId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeavePolicyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateLeavePolicyCommand(
                request.Name,
                request.Description,
                request.Country,
                request.JobLevel,
                request.AccrualMethod,
                request.AccrualStart,
                request.AccrualAfterNMonths,
                request.ProrationMethod,
                request.ProbationRestriction,
                request.MinimumTenureMonths,
                request.FirstYearReducedPercent,
                request.MinimumNoticeDays,
                request.MaxConsecutiveDays,
                request.MinDaysPerRequest,
                request.MaxTeamAbsencePercent,
                request.ApprovalMode,
                request.EffectiveFrom,
                (request.LeaveTypes ?? []).Select(x => new LeavePolicyTypeRuleInput(
                    x.LeaveTypeId,
                    x.AnnualEntitlementDays,
                    x.MonthlyAccrualDays,
                    x.CarryForwardMaxDays,
                    x.CarryForwardExpiryMonths)).ToList(),
                (request.BlackoutPeriods ?? []).Select(x => new LeavePolicyBlackoutPeriodInput(
                    x.StartDate,
                    x.EndDate,
                    x.Reason)).ToList(),
                request.LegalEntityIds ?? [],
                request.ConfirmReplaceExistingLegalEntityAssignments),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{leavePolicyId:guid}/clone")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Clone(
        Guid leavePolicyId,
        [FromBody] CloneLeavePolicyRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CloneLeavePolicyCommand(
                leavePolicyId,
                request.Name,
                request.Country,
                request.LegalEntityIds,
                request.EffectiveFrom,
                request.ConfirmReplaceExistingLegalEntityAssignments),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
