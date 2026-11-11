using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The principal a planning delegate acts for: whose ids go into the access checks. The signed-in user does not change.</summary>
public sealed record DelegateSubject(Guid EmployeeId, Guid UserId);

/// <summary>
/// Delegate task planning (2026-11-11), "The delegate check": the one place that decides whether the caller may plan,
/// and read for planning, on behalf of <c>forEmployeeId</c>. It reads the grant from the database on every call, so a
/// revoke, an expiry or the permission switched off takes effect on the next request.
/// </summary>
public interface IDelegateAccess
{
    /// <summary>The principal, when the caller is their active planning delegate now; otherwise null (callers answer 404).</summary>
    Task<DelegateSubject?> PlanningSubjectAsync(Guid tenantId, Guid callerEmployeeId, Guid forEmployeeId, CancellationToken ct);

    /// <summary>The task is the principal's assigned task or its parent.</summary>
    Task<bool> CanReadTaskAsync(Guid tenantId, Guid principalEmployeeId, Guid taskId, CancellationToken ct);

    /// <summary>The project holds a task assigned to the principal.</summary>
    Task<bool> CanReadProjectAsync(Guid tenantId, Guid principalEmployeeId, Guid projectId, CancellationToken ct);
}

public sealed class DelegateAccess : IDelegateAccess
{
    private readonly IMeetingDelegateRepository _delegates;
    private readonly IDelegateScopeRepository _scope;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public DelegateAccess(IMeetingDelegateRepository delegates, IDelegateScopeRepository scope, IEmployeeRepository employees,
        IDateTimeProvider clock)
    {
        _delegates = delegates;
        _scope = scope;
        _employees = employees;
        _clock = clock;
    }

    public async Task<DelegateSubject?> PlanningSubjectAsync(Guid tenantId, Guid callerEmployeeId, Guid forEmployeeId, CancellationToken ct)
    {
        // Oneself is not a delegated request: callers use their own path.
        if (forEmployeeId == callerEmployeeId
            || !await _delegates.IsActivePlanningDelegateAsync(tenantId, forEmployeeId, callerEmployeeId, _clock.UtcNow, ct))
            return null;
        var principal = await _employees.GetByIdAsync(tenantId, forEmployeeId, ct);
        return principal is null ? null : new DelegateSubject(principal.Id, principal.UserId);
    }

    public Task<bool> CanReadTaskAsync(Guid tenantId, Guid principalEmployeeId, Guid taskId, CancellationToken ct)
        => _scope.IsAssignedOrParentOfAssignedAsync(tenantId, principalEmployeeId, taskId, ct);

    public Task<bool> CanReadProjectAsync(Guid tenantId, Guid principalEmployeeId, Guid projectId, CancellationToken ct)
        => _scope.HasAssignedTaskInProjectAsync(tenantId, principalEmployeeId, projectId, ct);
}
