using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;

public interface ITenantConfigurationTemplateApplicationRepository
{
    Task AddAsync(TenantConfigurationTemplateApplication application, CancellationToken ct = default);
    Task<IReadOnlyList<TenantConfigurationTemplateApplication>> ListByTenantAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct = default);
    Task<int> CountByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantConfigurationTemplateApplication>> ListByTemplateAsync(
        Guid configurationTemplateId,
        CancellationToken ct = default);
}
