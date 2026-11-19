using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Access;

public interface IEmployeeInvitationAccess
{
    /// <summary>The user who added this employee (started the onboarding draft), or null when unknown.</summary>
    Task<Guid?> ResolveInviterUserIdAsync(InvitationToken invitation, Guid employeeCreatedById, CancellationToken ct = default);

    /// <summary>True when the caller may resend or revoke this employee's onboarding invitation.</summary>
    Task<bool> CanManageAsync(InvitationToken invitation, Guid employeeCreatedById, CancellationToken ct = default);
}

/// <summary>
/// Who may resend or revoke an employee onboarding invitation, with no dedicated permission code:
/// the person who added the employee, or anyone holding users:manage (Settings &gt; Users).
/// "Who added" is the onboarding draft's StartedById - not InvitationToken.CreatedById or
/// Employee.CreatedById, which both become the approver in the access-grant flow. Falls back to
/// Employee.CreatedById only for employees with no draft behind their invitation.
/// </summary>
public sealed class EmployeeInvitationAccess : IEmployeeInvitationAccess
{
    public const string ManagePermission = "users:manage";

    private readonly IOnboardingDraftRepository _drafts;
    private readonly ICurrentUser _currentUser;

    public EmployeeInvitationAccess(IOnboardingDraftRepository drafts, ICurrentUser currentUser)
    {
        _drafts = drafts;
        _currentUser = currentUser;
    }

    public async Task<Guid?> ResolveInviterUserIdAsync(
        InvitationToken invitation, Guid employeeCreatedById, CancellationToken ct = default)
    {
        if (invitation.OnboardingDraftId is Guid draftId)
        {
            var draft = await _drafts.GetTrackedAsync(invitation.TenantId, draftId, ct);
            if (draft is not null)
                return draft.StartedById;
        }

        return employeeCreatedById == Guid.Empty ? null : employeeCreatedById;
    }

    public async Task<bool> CanManageAsync(
        InvitationToken invitation, Guid employeeCreatedById, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return false;
        if (_currentUser.HasPermission(ManagePermission))
            return true;

        var inviter = await ResolveInviterUserIdAsync(invitation, employeeCreatedById, ct);
        return inviter == _currentUser.UserId;
    }
}
