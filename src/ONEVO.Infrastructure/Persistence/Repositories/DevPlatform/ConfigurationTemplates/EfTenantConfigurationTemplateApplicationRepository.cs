using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;

public sealed class EfTenantConfigurationTemplateApplicationRepository : ITenantConfigurationTemplateApplicationRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantConfigurationTemplateApplicationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TenantConfigurationTemplateApplication application, CancellationToken ct = default)
    {
        await _db.TenantConfigurationTemplateApplications.AddAsync(application, ct);
    }

    public async Task<IReadOnlyList<TenantConfigurationTemplateApplication>> ListByTenantAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var items = await _db.TenantConfigurationTemplateApplications
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.AppliedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return items;
    }

    public async Task<int> CountByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var count = await _db.TenantConfigurationTemplateApplications.CountAsync(a => a.TenantId == tenantId, ct);
        return count;
    }

    public async Task<IReadOnlyList<TenantConfigurationTemplateApplication>> ListByTemplateAsync(
        Guid configurationTemplateId,
        CancellationToken ct = default)
    {
        var items = await _db.TenantConfigurationTemplateApplications
            .Where(a => a.ConfigurationTemplateId == configurationTemplateId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(ct);
        return items;
    }
}
