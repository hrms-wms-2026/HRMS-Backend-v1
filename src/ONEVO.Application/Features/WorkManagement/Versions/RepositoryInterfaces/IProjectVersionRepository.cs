using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;

public interface IProjectVersionRepository
{
    Task AddAsync(ProjectVersion version, CancellationToken ct = default);
}
