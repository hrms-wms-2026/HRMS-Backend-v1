using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IWorkAreaChangeRequestRepository
{
    Task AddAsync(WorkAreaChangeRequest request, CancellationToken ct = default);
    Task<WorkAreaChangeRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<WorkAreaChangeRequest?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<WorkAreaChangeRequest> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListPendingEmployeeIdsAsync(
        Guid tenantId, Guid legalEntityId, DateOnly? from, DateOnly? to,
        CancellationToken ct = default);

    Task<(IReadOnlyList<WorkAreaChangeRequest> Items, int TotalCount)> ListApprovalInboxAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from, DateOnly? to, int skip, int take, CancellationToken ct = default);
    Task<bool> HasActiveForDateAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Returns the single approved override for the exact tenant/legal-entity/employee/date, or
    /// null when none exists. Throws <see cref="ONEVO.Application.Common.Exceptions.InconsistentWorkAreaChangeRequestStateException"/>
    /// if more than one approved row is found; the partial unique index should make that
    /// impossible in practice, so this is a fail-closed guard rather than an expected path.
    /// </summary>
    Task<WorkAreaChangeRequest?> GetApprovedForDateAsync(
        Guid tenantId, Guid legalEntityId, Guid employeeId, DateOnly date, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
