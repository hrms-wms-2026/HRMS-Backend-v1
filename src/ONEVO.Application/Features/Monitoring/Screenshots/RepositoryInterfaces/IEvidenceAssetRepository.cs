using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

public interface IEvidenceAssetRepository
{
    void Add(MonitoringEvidenceAsset asset);

    Task<MonitoringEvidenceAsset?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<(List<MonitoringEvidenceAsset> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        Guid? employeeId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct);
}
