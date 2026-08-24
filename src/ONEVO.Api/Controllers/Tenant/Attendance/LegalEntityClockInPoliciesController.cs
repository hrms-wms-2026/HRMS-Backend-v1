using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Attendance.ClockInPolicies;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Application.Features.TimeAttendance.Queries.GetClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Queries.ListClockInPolicies;

namespace ONEVO.Api.Controllers.Tenant.Attendance;

/// <summary>
/// Legal-entity-scoped Clock-in Policy mutations and reads.
/// Company context is route-scoped (same pattern as Departments/Positions).
/// </summary>
[ApiController]
[Route("api/v1/attendance/legal-entities/{legalEntityId:guid}/clock-in-policies")]
[Authorize(Policy = "TenantPolicy")]
public class LegalEntityClockInPoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LegalEntityClockInPoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> List(
        Guid legalEntityId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListClockInPoliciesQuery(legalEntityId, includeInactive), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> Get(
        Guid legalEntityId,
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetClockInPolicyQuery(legalEntityId, id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Create(
        Guid legalEntityId,
        [FromBody] UpsertClockInPolicyRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(ToCreateCommand(legalEntityId, request), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { legalEntityId, id = result.Value!.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Update(
        Guid legalEntityId,
        Guid id,
        [FromBody] UpsertClockInPolicyRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(ToUpdateCommand(legalEntityId, id, request), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/archive")]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Archive(
        Guid legalEntityId,
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ArchiveClockInPolicyCommand(legalEntityId, id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("attendance:write")]
    public async Task<IActionResult> Restore(
        Guid legalEntityId,
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RestoreClockInPolicyCommand(legalEntityId, id), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    private static CreateClockInPolicyCommand ToCreateCommand(
        Guid legalEntityId, UpsertClockInPolicyRequest request)
        => new(
            legalEntityId,
            request.Name,
            ToScope(request.Scope),
            request.EffectiveFrom,
            request.EffectiveTo,
            request.LocationVerificationRequired,
            request.AllowedRadiusMeters,
            ToWorkAreaRules(request.WorkAreaRules),
            request.CorrectionRequiresApproval,
            request.NotificationRecipientResolver,
            ToLateRules(request.LateDeductionRules),
            request.IsActive);

    private static UpdateClockInPolicyCommand ToUpdateCommand(
        Guid legalEntityId, Guid id, UpsertClockInPolicyRequest request)
        => new(
            legalEntityId,
            id,
            request.Name,
            ToScope(request.Scope),
            request.EffectiveFrom,
            request.EffectiveTo,
            request.LocationVerificationRequired,
            request.AllowedRadiusMeters,
            ToWorkAreaRules(request.WorkAreaRules),
            request.CorrectionRequiresApproval,
            request.NotificationRecipientResolver,
            ToLateRules(request.LateDeductionRules),
            request.IsActive);

    private static ClockInPolicyScopeInput ToScope(ClockInPolicyScopeRequest scope)
        => new(scope.Type, scope.DepartmentIds, scope.PositionIds, scope.EmployeeIds);

    private static WorkAreaRulesInput ToWorkAreaRules(WorkAreaRulesRequest rules)
        => new(
            new WorkAreaSourceRulesInput(
                rules.Onsite.BiometricEnabled,
                rules.Onsite.WebEnabled,
                rules.Onsite.TrayEnabled,
                rules.Onsite.PhotoRequired),
            new WorkAreaSourceRulesInput(
                rules.Remote.BiometricEnabled,
                rules.Remote.WebEnabled,
                rules.Remote.TrayEnabled,
                rules.Remote.PhotoRequired),
            new HybridWorkAreaRulesInput(
                rules.Hybrid.BiometricEnabled,
                rules.Hybrid.WebEnabled,
                rules.Hybrid.TrayEnabled,
                rules.Hybrid.PhotoRequired,
                rules.Hybrid.LocationCheckRequired,
                rules.Hybrid.SourceRule),
            new FieldWorkAreaRulesInput(
                rules.Field.BiometricEnabled,
                rules.Field.WebEnabled,
                rules.Field.TrayEnabled,
                rules.Field.PhotoRequirement));

    private static IReadOnlyList<LateDeductionRuleInput> ToLateRules(
        IReadOnlyList<LateDeductionRuleRequest>? rules)
        => (rules ?? Array.Empty<LateDeductionRuleRequest>())
            .Select(r => new LateDeductionRuleInput(r.LateArrivalMinute, r.Multiplier, r.TimeOffTypeId))
            .ToList();
}
