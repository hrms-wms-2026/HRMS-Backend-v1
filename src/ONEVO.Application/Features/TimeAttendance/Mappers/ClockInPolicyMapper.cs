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
            entity.LocationVerificationRequired,
            entity.AllowedRadiusMeters,
            new WorkAreaRulesResponse(
                new WorkAreaSourceRulesResponse(
                    entity.OnsiteBiometricEnabled,
                    entity.OnsiteWebEnabled,
                    entity.OnsiteTrayEnabled,
                    entity.OnsitePhotoRequired),
                new WorkAreaSourceRulesResponse(
                    entity.RemoteBiometricEnabled,
                    entity.RemoteWebEnabled,
                    entity.RemoteTrayEnabled,
                    entity.RemotePhotoRequired),
                new HybridWorkAreaRulesResponse(
                    entity.EitherBiometricEnabled,
                    entity.EitherWebEnabled,
                    entity.EitherTrayEnabled,
                    entity.EitherPhotoRequired,
                    entity.EitherLocationCheckRequired,
                    entity.EitherSourceRule),
                new FieldWorkAreaRulesResponse(
                    entity.FieldBiometricEnabled,
                    entity.FieldWebEnabled,
                    entity.FieldTrayEnabled,
                    entity.FieldPhotoRequirement)),
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

    public static void ApplyWorkAreaRules(ClockInPolicy entity, WorkAreaRulesInput rules)
    {
        entity.OnsiteBiometricEnabled = rules.Onsite.BiometricEnabled;
        entity.OnsiteWebEnabled = rules.Onsite.WebEnabled;
        entity.OnsiteTrayEnabled = rules.Onsite.TrayEnabled;
        entity.OnsitePhotoRequired = rules.Onsite.PhotoRequired;

        entity.RemoteBiometricEnabled = rules.Remote.BiometricEnabled;
        entity.RemoteWebEnabled = rules.Remote.WebEnabled;
        entity.RemoteTrayEnabled = rules.Remote.TrayEnabled;
        entity.RemotePhotoRequired = rules.Remote.PhotoRequired;

        // API/UI "hybrid" maps to inventory either_* persistence columns.
        entity.EitherBiometricEnabled = rules.Hybrid.BiometricEnabled;
        entity.EitherWebEnabled = rules.Hybrid.WebEnabled;
        entity.EitherTrayEnabled = rules.Hybrid.TrayEnabled;
        entity.EitherPhotoRequired = rules.Hybrid.PhotoRequired;
        entity.EitherLocationCheckRequired = rules.Hybrid.LocationCheckRequired;
        entity.EitherSourceRule = rules.Hybrid.SourceRule.Trim();

        entity.FieldBiometricEnabled = rules.Field.BiometricEnabled;
        entity.FieldWebEnabled = rules.Field.WebEnabled;
        entity.FieldTrayEnabled = rules.Field.TrayEnabled;
        entity.FieldPhotoRequirement = rules.Field.PhotoRequirement.Trim();
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
