using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Access;

public interface IEmployeeViewAccess
{
    /// <summary>True when the caller may view this employee's records (profile widgets, history, checklists).</summary>
    Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default);
}

/// <summary>
/// Relationship-based employee visibility, with no permission code: you can view yourself and
/// every employee inside your management coverage - the holders of the positions your position
/// covers plus their whole reporting-line subtree, department and company coverage included
/// (IEmployeeVisibilityScopeResolver). This is the same rule GetEmployee/GetEmployeeDetail apply,
/// so the widgets agree with the detail page.
///
/// Deliberately NOT IEmployeeAuthorityResolver.ResolveVisibilityAsync: that one keeps only
/// employment status "active", so an employee who is on leave, suspended or mid-offboarding
/// vanished and their manager got 403 on the offboarding wizard the moment step 1 ran.
/// </summary>
public sealed class EmployeeViewAccess : IEmployeeViewAccess
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;
    private readonly RepositoryInterfaces.IEmployeeRepository _visibleEmployees;
    private readonly IEmployeeVisibilityScopeResolver _scopeResolver;
    private readonly IInvitationTokenRepository _invitations;
    private readonly IEmployeeInvitationAccess _invitationAccess;
    private readonly ICurrentUser _currentUser;

    public EmployeeViewAccess(
        Common.RepositoryInterfaces.IEmployeeRepository employees,
        RepositoryInterfaces.IEmployeeRepository visibleEmployees,
        IEmployeeVisibilityScopeResolver scopeResolver,
        IInvitationTokenRepository invitations,
        IEmployeeInvitationAccess invitationAccess,
        ICurrentUser currentUser)
    {
        _employees = employees;
        _visibleEmployees = visibleEmployees;
        _scopeResolver = scopeResolver;
        _invitations = invitations;
        _invitationAccess = invitationAccess;
        _currentUser = currentUser;
    }

    public async Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
            return false;

        var tenantId = _currentUser.TenantId;
        var self = await _employees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (self is not null && self.Id == employeeId)
            return true;

        var scope = await _scopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);
        if (await _visibleEmployees.GetVisibleByIdAsync(tenantId, scope, employeeId, ct) is not null)
            return true;

        // Same exception GetEmployeeDetail makes: while an invite is open, the person who added the
        // employee can open them even without coverage (their position is still "planned", so no
        // reporting line reaches them yet) - otherwise the detail page loads but every widget 403s.
        var invitation = await _invitations.GetLatestByEmployeeIdAsync(tenantId, employeeId, ct);
        if (invitation is not { UsedAt: null, RevokedAt: null })
            return false;

        var subject = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        return subject is not null
            && await _invitationAccess.ResolveInviterUserIdAsync(invitation, subject.CreatedById, ct) == _currentUser.UserId;
    }
}
