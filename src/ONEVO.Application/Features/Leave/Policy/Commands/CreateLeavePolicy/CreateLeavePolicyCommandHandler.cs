using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Helpers;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public class CreateLeavePolicyCommandHandler : IRequestHandler<CreateLeavePolicyCommand, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateLeavePolicyCommandHandler(
        ILeavePolicyRepository policies,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(CreateLeavePolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LeavePolicyResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingLeavePolicyId: null, ct))
            return Result<LeavePolicyResponse>.Conflict("A policy with this name already exists");

        var requestedLeaveTypeIds = request.LeaveTypes.Select(x => x.LeaveTypeId).Distinct().ToArray();
        var activeLeaveTypes = await _policies.ListActiveLeaveTypesByIdsAsync(tenantId, requestedLeaveTypeIds, ct);
        if (activeLeaveTypes.Count != requestedLeaveTypeIds.Length)
            return Result<LeavePolicyResponse>.NotFound("The selected leave type no longer exists.");

        var leaveTypeById = activeLeaveTypes.ToDictionary(x => x.Id);
        foreach (var rule in request.LeaveTypes)
        {
            var annualEntitlement = ToAnnualEntitlement(request.AccrualMethod, rule);
            var leaveType = leaveTypeById[rule.LeaveTypeId];
            if (request.AccrualMethod == LeaveAccrualMethods.Monthly && annualEntitlement > leaveType.DefaultDaysPerYear)
            {
                return Result<LeavePolicyResponse>.Failure(
                    $"Monthly accrual ({rule.MonthlyAccrualDays:0.#} x 12 = {annualEntitlement:0.#} days) exceeds the leave type's annual limit of {leaveType.DefaultDaysPerYear:0.#} days");
            }
        }

        var requestedLegalEntityIds = request.LegalEntityIds.Distinct().ToArray();
        var legalEntities = await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, requestedLegalEntityIds, ct);
        if (legalEntities.Count != requestedLegalEntityIds.Length)
            return Result<LeavePolicyResponse>.NotFound("Legal entity not found.");

        var conflicts = await _policies.ListActiveAssignmentConflictsAsync(tenantId, requestedLegalEntityIds, ct);
        if (conflicts.Count > 0 && !request.ConfirmReplaceExistingLegalEntityAssignments)
            return Result<LeavePolicyResponse>.Conflict(LeavePolicyConflictMessages.BuildReplacementConflictMessage(conflicts));

        var policyId = Guid.NewGuid();
        var now = _dateTimeProvider.UtcNow;
        var policy = new LeavePolicy
        {
            Id = policyId,
            TenantId = tenantId,
            Name = name,
            Description = request.Description?.Trim(),
            Country = request.Country.Trim(),
            JobLevel = string.IsNullOrWhiteSpace(request.JobLevel) ? null : request.JobLevel.Trim(),
            AccrualMethod = request.AccrualMethod,
            AccrualStart = request.AccrualStart,
            AccrualAfterNMonths = request.AccrualAfterNMonths,
            ProrationMethod = request.ProrationMethod,
            ProbationRestriction = request.ProbationRestriction,
            MinimumTenureMonths = request.MinimumTenureMonths,
            FirstYearReducedPercent = request.FirstYearReducedPercent,
            MinimumNoticeDays = request.MinimumNoticeDays,
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            MinDaysPerRequest = request.MinDaysPerRequest,
            MaxTeamAbsencePercent = request.MaxTeamAbsencePercent,
            ApprovalMode = request.ApprovalMode,
            EffectiveFrom = request.EffectiveFrom,
            Version = 1,
            IsActive = true,
            CreatedAt = now
        };

        var typeRules = request.LeaveTypes.Select(rule => new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            LeaveTypeId = rule.LeaveTypeId,
            AnnualEntitlementDays = ToAnnualEntitlement(request.AccrualMethod, rule),
            CarryForwardMaxDays = rule.CarryForwardMaxDays,
            CarryForwardExpiryMonths = rule.CarryForwardExpiryMonths
        }).ToList();

        var blackoutPeriods = request.BlackoutPeriods.Select(period => new LeavePolicyBlackoutPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Reason = period.Reason?.Trim()
        }).ToList();

        var assignments = requestedLegalEntityIds.Select(legalEntityId => new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            LegalEntityId = legalEntityId,
            EffectiveDate = request.EffectiveFrom,
            IsActive = true
        }).ToList();

        var replacementIds = request.ConfirmReplaceExistingLegalEntityAssignments
            ? conflicts.Select(c => c.LegalEntityId).Distinct().ToArray()
            : [];

        try
        {
            await _policies.AddAggregateWithReplacementAsync(policy, typeRules, blackoutPeriods, assignments, replacementIds, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<LeavePolicyResponse>.Conflict(
                "Legal Entity already has an active policy. Activating this policy will replace it. Continue?");
        }

        var aggregate = await _policies.GetAggregateByIdAsync(tenantId, policyId, ct);
        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate!));
    }

    private static decimal ToAnnualEntitlement(string accrualMethod, LeavePolicyTypeRuleInput rule)
        => accrualMethod == LeaveAccrualMethods.Monthly
            ? decimal.Round(rule.MonthlyAccrualDays!.Value * 12m, 1, MidpointRounding.AwayFromZero)
            : rule.AnnualEntitlementDays;
}
