using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;

public interface ICheckInRepository
{
    Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct);
    Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct);
    Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct);
    Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct);
}
