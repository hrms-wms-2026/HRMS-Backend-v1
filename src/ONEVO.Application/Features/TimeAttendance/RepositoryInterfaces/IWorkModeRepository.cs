using TimeAttendanceWorkMode = ONEVO.Domain.Features.TimeAttendance.Entities.WorkMode;

namespace ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

public interface IWorkModeRepository
{
    Task<IReadOnlyList<TimeAttendanceWorkMode>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default);

    Task<TimeAttendanceWorkMode?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<TimeAttendanceWorkMode?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task<bool> NameExistsAsync(
        Guid tenantId, Guid legalEntityId, string name, Guid? excludingId, CancellationToken ct = default);

    Task AddAsync(TimeAttendanceWorkMode workMode, CancellationToken ct = default);

    void Update(TimeAttendanceWorkMode workMode);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
