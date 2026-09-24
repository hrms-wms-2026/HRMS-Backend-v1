using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Helpers;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public class CloneLeavePolicyCommandHandler : IRequestHandler<CloneLeavePolicyCommand, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CloneLeavePolicyCommandHandler(
        ILeavePolicyRepository policies,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(CloneLeavePolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var source = await _policies.GetAggregateByIdAsync(tenantId, request.SourcePolicyId, ct);
        if (source is null)
            return Result<LeavePolicyResponse>.NotFound("Leave policy not found.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingLeavePolicyId: null, ct))
            return Result<LeavePolicyResponse>.Conflict("A policy with this name already exists");

        var sourceLeaveTypeIds = source.LeaveTypes.Select(x => x.Rule.LeaveTypeId).Distinct().ToArray();
        if (sourceLeaveTypeIds.Length > 0)
        {
            var activeLeaveTypes = await _policies.ListActiveLeaveTypesByIdsAsync(tenantId, sourceLeaveTypeIds, ct);
            if (activeLeaveTypes.Count != sourceLeaveTypeIds.Length)
                return Result<LeavePolicyResponse>.NotFound("The selected leave type no longer exists.");
        }

        var requestedLegalEntityIds = request.LegalEntityIds.Distinct().ToArray();
        var legalEntities = await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, requestedLegalEntityIds, ct);
        if (legalEntities.Count != requestedLegalEntityIds.Length)
            return Result<LeavePolicyResponse>.NotFound("Legal entity not found.");

        var conflicts = await _policies.ListActiveAssignmentConflictsAsync(tenantId, requestedLegalEntityIds, ct);
        if (conflicts.Count > 0 && !request.ConfirmReplaceExistingLegalEntityAssignments)
            return Result<LeavePolicyResponse>.Conflict(LeavePolicyConflictMessages.BuildReplacementConflictMessage(conflicts));

        var newPolicyId = Guid.NewGuid();
        var original = source.Policy;
        var clone = new LeavePolicy
        {
            Id = newPolicyId,
            TenantId = tenantId,
            Name = name,
            Description = original.Description,
            Country = request.Country.Trim(),
            JobLevel = original.JobLevel,
            AccrualMethod = original.AccrualMethod,
            AccrualStart = original.AccrualStart,
            AccrualAfterNMonths = original.AccrualAfterNMonths,
            ProrationMethod = original.ProrationMethod,
            ProbationRestriction = original.ProbationRestriction,
            MinimumTenureMonths = original.MinimumTenureMonths,
            FirstYearReducedPercent = original.FirstYearReducedPercent,
            MinimumNoticeDays = original.MinimumNoticeDays,
            MaxConsecutiveDays = original.MaxConsecutiveDays,
            MinDaysPerRequest = original.MinDaysPerRequest,
            MaxTeamAbsencePercent = original.MaxTeamAbsencePercent,
            ApprovalMode = original.ApprovalMode,
            EffectiveFrom = request.EffectiveFrom,
            Version = 1,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        var typeRules = source.LeaveTypes.Select(item => new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            LeaveTypeId = item.Rule.LeaveTypeId,
            AnnualEntitlementDays = item.Rule.AnnualEntitlementDays,
            CarryForwardMaxDays = item.Rule.CarryForwardMaxDays,
            CarryForwardExpiryMonths = item.Rule.CarryForwardExpiryMonths
        }).ToList();

        var blackoutPeriods = source.BlackoutPeriods.Select(period => new LeavePolicyBlackoutPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Reason = period.Reason
        }).ToList();

        var assignments = requestedLegalEntityIds.Select(legalEntityId => new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            LegalEntityId = legalEntityId,
            EffectiveDate = request.EffectiveFrom,
            IsActive = true
        }).ToList();

        var replacementIds = request.ConfirmReplaceExistingLegalEntityAssignments
            ? conflicts.Select(c => c.LegalEntityId).Distinct().ToArray()
            : [];

        try
        {
            await _policies.AddAggregateWithReplacementAsync(clone, typeRules, blackoutPeriods, assignments, replacementIds, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<LeavePolicyResponse>.Conflict(
                "Legal Entity already has an active policy. Activating this policy will replace it. Continue?");
        }

        var aggregate = await _policies.GetAggregateByIdAsync(tenantId, newPolicyId, ct);
        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate!));
    }
}
