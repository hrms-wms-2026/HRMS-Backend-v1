using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;

public interface ICheckInRepository
{
    Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct);
    Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct);
    Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct);
    Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct);

    /// <summary>Check-ins for one user within a local-day time window, oldest first - used to show
    /// captured check-in locations on the attendance day-detail read.</summary>
    Task<IReadOnlyList<EmployeeCheckIn>> ListForUserInRangeAsync(
        Guid tenantId, Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Batch variant of <see cref="ListForUserInRangeAsync"/> - check-ins for several users
    /// within the same time window, used to detect camera-verification-skipped check-ins across a page
    /// of employees without one query per row.</summary>
    Task<IReadOnlyList<EmployeeCheckIn>> ListForUsersInRangeAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
