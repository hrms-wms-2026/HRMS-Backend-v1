using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfRoleRepository : IRoleRepository
{
    private readonly ApplicationDbContext _db;

    public EfRoleRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Role?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        return role;
    }

    public async Task<Role?> GetByIdForTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);
        return role;
    }

    public async Task<Role?> GetByNameForTenantAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == name, ct);
        return role;
    }

    public async Task<Role?> GetBySourceTemplateForTenantAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.SourceTemplateId == templateId,
            ct);
        return role;
    }

    public async Task<IReadOnlyList<Role>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var roles = await _db.Roles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
        return roles;
    }

    public Task AddAsync(Role role, CancellationToken ct = default)
    {
        var addTask = _db.Roles.AddAsync(role, ct).AsTask();
        return addTask;
    }

    public void Remove(Role role)
    {
        _db.Roles.Remove(role);
    }
}
