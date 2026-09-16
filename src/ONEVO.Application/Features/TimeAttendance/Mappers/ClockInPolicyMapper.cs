using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Models;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Mappers;

public static class ClockInPolicyMapper
{
    public static ClockInPolicyResponse ToResponse(ClockInPolicy entity)
    {
        var rules = entity.LateDeductionRules
            .OrderBy(r => r.LateArrivalMinute)
            .Select(r => new LateDeductionRuleResponse(
                r.Id,
                r.LateArrivalMinute,
                r.Multiplier,
                r.TimeOffTypeId,
                r.IsActive))
            .ToList();

        return new ClockInPolicyResponse(
            entity.Id,
            entity.LegalEntityId,
            entity.Name,
            new ClockInPolicyScopeResponse(
                entity.ScopeType,
                entity.DepartmentIds ?? Array.Empty<Guid>(),
                entity.PositionIds ?? Array.Empty<Guid>(),
                entity.EmployeeIds ?? Array.Empty<Guid>()),
            entity.EffectiveFrom,
            entity.EffectiveTo,
            entity.CorrectionRequiresApproval,
            entity.NotificationRecipientResolver,
            rules,
            entity.IsActive,
            entity.CreatedById,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static ClockInPolicyListItemResponse ToListItem(ClockInPolicy entity)
    {
        return new ClockInPolicyListItemResponse(
            entity.Id,
            entity.LegalEntityId,
            entity.Name,
            entity.ScopeType,
            entity.EffectiveFrom,
            entity.EffectiveTo,
            entity.IsActive,
            entity.LateDeductionRules.Count,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static void ApplyScope(ClockInPolicy entity, ClockInPolicyScopeInput scope)
    {
        entity.ScopeType = scope.Type.Trim();
        entity.DepartmentIds = NormalizeIds(scope.DepartmentIds);
        entity.PositionIds = NormalizeIds(scope.PositionIds);
        entity.EmployeeIds = NormalizeIds(scope.EmployeeIds);
    }

    public static Guid[]? NormalizeIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
            return null;

        return ids.Distinct().ToArray();
    }
}
