using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

public sealed class EfRoleTemplateRepository : IRoleTemplateRepository
{
    private readonly ApplicationDbContext _db;

    public EfRoleTemplateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RoleTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var template = await _db.RoleTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        return template;
    }

    public async Task<RoleTemplate?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var n = name.Trim();
        var template = await _db.RoleTemplates.FirstOrDefaultAsync(
            t => t.Name.ToLower() == n.ToLower(),
            ct);
        return template;
    }

    public async Task<IReadOnlyList<RoleTemplate>> ListAsync(CancellationToken ct = default)
    {
        var query = _db.RoleTemplates
            .OrderBy(t => t.Name);

        var templates = await query.ToListAsync(ct);
        return templates;
    }

    public Task AddAsync(RoleTemplate template, CancellationToken ct = default)
    {
        var addTask = _db.RoleTemplates.AddAsync(template, ct).AsTask();
        return addTask;
    }
}
