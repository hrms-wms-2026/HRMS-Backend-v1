namespace ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;

/// <summary>
/// The exception alerts the current user may see or act on. HR (<see cref="IsHr"/>) sees every
/// employee in the tenant; anyone else sees only the employees in <see cref="EmployeeIds"/>,
/// which IEmployeeAuthorityResolver derived from management coverage and approver routing. Nobody
/// ever works an alert about themselves.
/// </summary>
public sealed record ExceptionScope(bool IsHr, Guid? ActorEmployeeId, IReadOnlyCollection<Guid> EmployeeIds)
{
    public bool CanSee(Guid employeeId) =>
        employeeId != ActorEmployeeId && (IsHr || EmployeeIds.Contains(employeeId));
}

public interface IExceptionScopeResolver
{
    /// <summary>Null when the current user has no access at all (not signed in, or no manager /
    /// HR / exceptions permission). <paramref name="forAction"/> selects the acknowledge
    /// permission instead of the view permission for users who rely on the exceptions:* codes.
    /// <paramref name="candidateEmployeeIds"/> are the employees the caller is asking about: a
    /// manager also sees any of them they are the exact approver for, so whoever the alert was
    /// routed to (reporting line included) can always open and work it.</summary>
    Task<ExceptionScope?> ResolveAsync(
        bool forAction, IReadOnlyCollection<Guid> candidateEmployeeIds, CancellationToken ct);
}
