using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetTenantById;

public class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, Result<TenantDetailDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly ILegalEntityRepository _legalEntities;

    public GetTenantByIdQueryHandler(
        ITenantRepository tenants,
        ILegalEntityRepository legalEntities)
    {
        _tenants = tenants;
        _legalEntities = legalEntities;
    }

    public async Task<Result<TenantDetailDto>> Handle(GetTenantByIdQuery request, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<TenantDetailDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var legalEntity = await _legalEntities.GetPrimaryByTenantIdAsync(tenant.Id, ct);

        return Result<TenantDetailDto>.Success(new TenantDetailDto(
            Id: tenant.Id,
            Name: tenant.Name,
            Slug: tenant.Slug,
            IndustryProfile: tenant.IndustryProfile,
            CompanySizeRange: tenant.CompanySizeRange,
            Status: tenant.Status.ToString().ToLowerInvariant(),
            SubscriptionPlanId: tenant.SubscriptionPlanId,
            SettingsJson: tenant.SettingsJson,
            LegalEntityName: legalEntity?.Name,
            RegistrationNumber: legalEntity?.RegistrationNumber,
            Country: legalEntity?.CountryCode,
            Currency: legalEntity?.CurrencyCode,
            CreatedAt: tenant.CreatedAt,
            UpdatedAt: tenant.UpdatedAt));
    }
}
