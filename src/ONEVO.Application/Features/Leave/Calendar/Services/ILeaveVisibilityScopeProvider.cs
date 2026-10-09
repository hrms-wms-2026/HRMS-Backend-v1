using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.Leave.Calendar.Services;

public enum LeaveVisibilityScopeFailure
{
    NoEmployee,
    NoLeaveReadPermission,
}

public sealed record LeaveVisibilityScopeResolution(EmployeeVisibilityScope? Scope, LeaveVisibilityScopeFailure? Failure);

/// <summary>The Leave domain's own answer to "whose leave may the current user see":
/// leave:read / leave:manage = everyone, leave:read-team = raw management coverage,
/// leave:read-own = self. Shared by the leave calendar and My Team's Team Status leave masking
/// (My Team spec §9.3).</summary>
public interface ILeaveVisibilityScopeProvider
{
    Task<LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default);
}
