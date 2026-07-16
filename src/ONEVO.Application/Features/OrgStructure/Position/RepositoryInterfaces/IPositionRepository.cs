using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
