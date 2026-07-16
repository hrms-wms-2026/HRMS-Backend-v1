using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantOneTimeCharges;

public sealed class GetTenantOneTimeChargesQueryHandler
    : IRequestHandler<GetTenantOneTimeChargesQuery, Result<IReadOnlyList<TenantOneTimeChargeDto>>>
{
    private readonly ITenantOneTimeChargeRepository _repo;

    public GetTenantOneTimeChargesQueryHandler(ITenantOneTimeChargeRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<TenantOneTimeChargeDto>>> Handle(
        GetTenantOneTimeChargesQuery request,
        CancellationToken ct)
    {
        var charges = await _repo.GetByTenantAsync(request.TenantId, ct);

        var dtos = charges.Select(BillingMapper.ToChargeDto).ToList();

        return Result<IReadOnlyList<TenantOneTimeChargeDto>>.Success(dtos);
    }
}
