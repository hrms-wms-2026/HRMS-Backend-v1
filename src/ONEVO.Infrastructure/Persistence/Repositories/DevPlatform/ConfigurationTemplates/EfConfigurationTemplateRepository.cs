using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ConfigurationTemplates;

public sealed class EfConfigurationTemplateRepository : IConfigurationTemplateRepository
{
    private readonly ApplicationDbContext _db;

    public EfConfigurationTemplateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ConfigurationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var template = await _db.ConfigurationTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        return template;
    }

    public async Task<ConfigurationTemplate?> GetByTemplateKeyAsync(string templateKey, CancellationToken ct = default)
    {
        var template = await _db.ConfigurationTemplates.FirstOrDefaultAsync(t => t.TemplateKey == templateKey, ct);
        return template;
    }

    public async Task<IReadOnlyList<ConfigurationTemplate>> ListAsync(
        string? templateType,
        bool? activeOnly,
        string? industryProfileTag,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(templateType, activeOnly, industryProfileTag);
        var items = await query.OrderBy(t => t.Name).Skip(skip).Take(take).ToListAsync(ct);
        return items;
    }

    public async Task<int> CountAsync(
        string? templateType,
        bool? activeOnly,
        string? industryProfileTag,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(templateType, activeOnly, industryProfileTag);
        var count = await query.CountAsync(ct);
        return count;
    }

    public async Task AddAsync(ConfigurationTemplate template, CancellationToken ct = default)
    {
        await _db.ConfigurationTemplates.AddAsync(template, ct);
    }

    private IQueryable<ConfigurationTemplate> BuildFilteredQuery(
        string? templateType,
        bool? activeOnly,
        string? industryProfileTag)
    {
        var query = _db.ConfigurationTemplates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(templateType))
        {
            query = query.Where(t => t.TemplateType == templateType);
        }

        if (activeOnly == true)
        {
            query = query.Where(t => t.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(industryProfileTag))
        {
            query = query.Where(t => t.IndustryProfileTag == industryProfileTag);
        }

        return query;
    }
}
