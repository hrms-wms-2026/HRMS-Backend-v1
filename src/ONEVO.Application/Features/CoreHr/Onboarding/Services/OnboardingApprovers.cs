using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Services;

/// <summary>A person who can decide an onboarding approval. EmployeeId is null for a user with no employee record.</summary>
public sealed record OnboardingApprover(Guid UserId, Guid? EmployeeId, string Name);

/// <summary>
/// Who decides the two onboarding approvals - the same people the existing flows already use: a position access
/// request goes to the roles:manage holders (the approve/reject endpoints' gate, and who gets the approval email);
/// a seat increase goes to the billing:manage holders. The requester never approves their own request.
/// </summary>
public sealed class OnboardingApprovers
{
    public const string PositionApprovalPermission = "roles:manage";
    public const string SeatIncreasePermission = "billing:manage";

    private readonly IPermissionRepository _permissions;
    private readonly IUserRepository _users;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public OnboardingApprovers(IPermissionRepository permissions, IUserRepository users, IEmployeeRepository employees, IDateTimeProvider clock)
    {
        _permissions = permissions;
        _users = users;
        _employees = employees;
        _clock = clock;
    }

    public Task<IReadOnlyList<OnboardingApprover>> PositionApproversAsync(Guid tenantId, Guid? requesterUserId, CancellationToken ct)
        => ListAsync(tenantId, PositionApprovalPermission, requesterUserId, ct);

    public Task<IReadOnlyList<OnboardingApprover>> SeatApproversAsync(Guid tenantId, Guid? requesterUserId, CancellationToken ct)
        => ListAsync(tenantId, SeatIncreasePermission, requesterUserId, ct);

    private async Task<IReadOnlyList<OnboardingApprover>> ListAsync(Guid tenantId, string permission, Guid? requesterUserId, CancellationToken ct)
    {
        var userIds = (await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, permission, _clock.UtcNow, ct))
            .Where(id => id != requesterUserId).Distinct().ToList();
        var approvers = new List<OnboardingApprover>(userIds.Count);
        foreach (var userId in userIds)
        {
            var user = await _users.GetByIdAsync(userId, ct);
            if (user is null)
                continue;
            var employee = await _employees.GetDefaultForUserAsync(tenantId, userId, ct);
            var name = employee is not null ? $"{employee.FirstName} {employee.LastName}".Trim() : $"{user.FirstName} {user.LastName}".Trim();
            approvers.Add(new OnboardingApprover(userId, employee?.Id, string.IsNullOrEmpty(name) ? user.Email : name));
        }
        return approvers.OrderBy(a => a.Name).ToList();
    }
}
