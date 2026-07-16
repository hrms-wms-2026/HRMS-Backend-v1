using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;

public interface IRoleTemplateRepository
{
    Task<RoleTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleTemplate?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<RoleTemplate>> ListAsync(CancellationToken ct = default);
    Task AddAsync(RoleTemplate template, CancellationToken ct = default);
}
