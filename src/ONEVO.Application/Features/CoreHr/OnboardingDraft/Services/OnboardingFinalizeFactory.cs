using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// The rows an onboarding draft turns into when it is finalized. Both finalize paths build them
/// here - the direct path (<see cref="OnboardingDraftWriteService"/>) and approval of an access
/// grant request (ApproveAccessGrantRequestCommandHandler) - so a new hire gets the same user,
/// employee, invitation and email whichever path ran. Pure builders only: each caller keeps its
/// own transaction, seat, manager and role decisions.
/// </summary>
public static class OnboardingFinalizeFactory
{
    public const int InvitationValidityHours = 24;

    public const string TemplateNoLongerAppliesMessage =
        "The selected onboarding checklist template does not exist, is inactive, or no longer applies to this company/department/position.";

    public const string InvalidChecklistTasksMessage =
        "Checklist task data is invalid: every task requires a title, a known owner type, a resolvable assignee, a due rule, and an explicit required flag.";

    /// <summary>The inactive user a new hire activates through the invitation. Not persisted here.</summary>
    public static User NewPendingUser(OnboardingDraftEntity draft) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = draft.TenantId,
        Email = draft.WorkEmail.Trim(),
        FirstName = draft.FirstName.Trim(),
        LastName = draft.LastName.Trim(),
        PasswordHash = string.Empty,
        IsActive = false,
        EmailVerified = false,
        MustChangePassword = true,
        PasswordSetByAdmin = false,
    };

    public static EmployeeEntity NewEmployee(
        OnboardingDraftEntity draft, Guid employeeId, Guid userId, int employmentTypeId, DateOnly hireDate, Guid actingUserId) => new()
    {
        Id = employeeId,
        TenantId = draft.TenantId,
        UserId = userId,
        EmployeeNumber = draft.EmployeeNumber!,
        FirstName = draft.FirstName.Trim(),
        LastName = draft.LastName.Trim(),
        Email = draft.WorkEmail.Trim(),
        DepartmentId = draft.DepartmentId,
        LegalEntityId = draft.LegalEntityId,
        // No "onboarding" row exists in the tenant-wide employment_statuses lookup (it is
        // read app-wide, e.g. the employee list falls back to "active"), so pending-ness is
        // carried by User.IsActive = false, InvitationToken.Status = "pending", and the
        // draft's own Status/FinalizedAt instead of an employee-level status value.
        EmploymentStatusId = 1,
        EmploymentTypeId = employmentTypeId,
        WorkModeId = draft.WorkModeId,
        HireDate = hireDate,
        CreatedById = actingUserId,
    };

    /// <summary>The clock-in evidence the wizard changed for this employee only, or null when it changed none.</summary>
    public static EmployeeMonitoringOverride? NewMonitoringOverride(
        OnboardingDraftEntity draft, Guid employeeId, Guid actingUserId, DateTimeOffset now)
    {
        if (!draft.PhotoRequiredOverride.HasValue && !draft.LocationCaptureOverride.HasValue)
            return null;

        return new EmployeeMonitoringOverride
        {
            Id = Guid.NewGuid(),
            TenantId = draft.TenantId,
            EmployeeId = employeeId,
            PhotoRequired = draft.PhotoRequiredOverride,
            WorkLocationVerification = draft.LocationCaptureOverride,
            OverrideReason = "Set during onboarding (Add Employee wizard).",
            SetById = actingUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static UserRole NewPositionUserRole(
        OnboardingDraftEntity draft, Guid userId, Guid roleId, Guid accessTemplateId, Guid actingUserId, DateTimeOffset now) => new()
    {
        TenantId = draft.TenantId,
        UserId = userId,
        RoleId = roleId,
        AssignedAt = now,
        AssignedBy = actingUserId,
        SourcePositionId = draft.PositionId,
        SourcePositionAccessTemplateId = accessTemplateId,
    };

    public static InvitationToken NewInvitation(
        OnboardingDraftEntity draft, Guid userId, Guid employeeId, Guid? positionAssignmentId,
        string tokenHash, DateTimeOffset expiresAt, Guid actingUserId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = draft.TenantId,
        UserId = userId,
        RoleId = null,
        PositionId = draft.PositionId,
        PositionAssignmentId = positionAssignmentId,
        Purpose = InvitationToken.EmployeeOnboardingPurpose,
        LegalEntityId = draft.LegalEntityId,
        EmployeeId = employeeId,
        OnboardingDraftId = draft.Id,
        InvitedEmail = draft.WorkEmail.Trim(),
        InvitedFullName = $"{draft.FirstName.Trim()} {draft.LastName.Trim()}".Trim(),
        TokenHash = tokenHash,
        ExpiresAt = expiresAt,
        CreatedAt = now,
        CreatedById = actingUserId,
    };

    public static EmployeeOnboardingInviteEmailPayload NewInviteEmailPayload(
        OnboardingDraftEntity draft, Guid employeeId, Guid invitationId, string rawToken, DateTimeOffset expiresAt, string? tenantSlug) =>
        new(
            draft.TenantId, draft.LegalEntityId, employeeId, invitationId,
            draft.WorkEmail.Trim(), draft.FirstName.Trim(), draft.LastName.Trim(), rawToken, expiresAt,
            tenantSlug);

    public static void MarkFinalized(OnboardingDraftEntity draft, DateTimeOffset now)
    {
        draft.Status = OnboardingDraftStatus.Finalized;
        draft.DraftReason = OnboardingDraftReason.InvitationSent;
        draft.FinalizedAt = now;
        draft.UpdatedAt = now;
    }
}
