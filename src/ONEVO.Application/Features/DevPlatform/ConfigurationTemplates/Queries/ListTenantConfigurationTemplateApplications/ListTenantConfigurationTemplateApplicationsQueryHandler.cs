using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Mappers;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListTenantConfigurationTemplateApplications;

public sealed class ListTenantConfigurationTemplateApplicationsQueryHandler
    : IRequestHandler<ListTenantConfigurationTemplateApplicationsQuery, Result<TenantConfigurationTemplateApplicationListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ITenantRepository _tenants;
    private readonly ITenantConfigurationTemplateApplicationRepository _applications;

    public ListTenantConfigurationTemplateApplicationsQueryHandler(
        ITenantRepository tenants,
        ITenantConfigurationTemplateApplicationRepository applications)
    {
        _tenants = tenants;
        _applications = applications;
    }

    public async Task<Result<TenantConfigurationTemplateApplicationListResponseDto>> Handle(
        ListTenantConfigurationTemplateApplicationsQuery request,
        CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
        {
            return Result<TenantConfigurationTemplateApplicationListResponseDto>.NotFound("Tenant not found.");
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var skip = (page - 1) * size;

        var applications = await _applications.ListByTenantAsync(request.TenantId, skip, size, ct);
        var total = await _applications.CountByTenantAsync(request.TenantId, ct);
        var items = applications.Select(ConfigurationTemplateMapper.ToDto).ToList();

        return Result<TenantConfigurationTemplateApplicationListResponseDto>.Success(
            ConfigurationTemplateMapper.ToListResponseDto(items, total, page, size));
    }
}
