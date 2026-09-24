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

    /// <summary>
    /// Screenshots whose <see cref="MonitoringEvidenceAsset.CapturedAt"/> falls in
    /// [<paramref name="from"/>, <paramref name="to"/>). <paramref name="ownerIds"/> is the
    /// CoreHR employee id plus the user id, because older tray uploads stored the user id
    /// when no employee row existed yet.
    /// </summary>
    Task<List<MonitoringEvidenceAsset>> ListForOwnersInRangeAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ownerIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int take,
        CancellationToken ct);
}
