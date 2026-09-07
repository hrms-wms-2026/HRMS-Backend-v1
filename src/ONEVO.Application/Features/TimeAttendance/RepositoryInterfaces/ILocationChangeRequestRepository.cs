using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface ILocationChangeRequestRepository
{
    Task AddAsync(LocationChangeRequest request, CancellationToken ct = default);
    Task<LocationChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<LocationChangeRequest?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>The employee's own currently-active request (pending or approved-but-not-yet-
    /// applied) - at most one can exist at a time per the unique index.</summary>
    Task<LocationChangeRequest?> GetActiveForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Tracked fetch of the employee's approved-but-not-yet-applied request, used by the
    /// post-clock-in "save as new location?" prompt. Null when there is none.</summary>
    Task<LocationChangeRequest?> GetTrackedApprovedUnappliedForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<(IReadOnlyList<LocationChangeRequest> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, string? status, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task<(IReadOnlyList<LocationChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        int skip, int take, CancellationToken ct = default);

    Task<bool> HasActiveAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
