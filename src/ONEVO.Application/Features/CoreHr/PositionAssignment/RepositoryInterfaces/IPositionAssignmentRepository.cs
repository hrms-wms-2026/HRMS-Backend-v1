namespace ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;

public interface IPositionAssignmentRepository
{
    Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetActivePrimaryAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid tenantId, Guid positionId, CancellationToken ct = default);

    Task<bool> HasActivePrimaryInLegalEntityAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, CancellationToken ct = default);

    Task AddAsync(ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment assignment, CancellationToken ct = default);

    Task<ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment?> GetTrackedAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
