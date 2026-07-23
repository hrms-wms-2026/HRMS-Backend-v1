using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

public interface IConfigurationTemplateRepository
{
    Task<ConfigurationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConfigurationTemplate?> GetByTemplateKeyAsync(string templateKey, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigurationTemplate>> ListAsync(
        string? templateType,
        bool? activeOnly,
        string? industryProfileTag,
        int skip,
        int take,
        CancellationToken ct = default);
    Task<int> CountAsync(
        string? templateType,
        bool? activeOnly,
        string? industryProfileTag,
        CancellationToken ct = default);
    Task AddAsync(ConfigurationTemplate template, CancellationToken ct = default);
}
