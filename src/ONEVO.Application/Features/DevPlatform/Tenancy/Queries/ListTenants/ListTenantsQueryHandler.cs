using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListTenants;

public class ListTenantsQueryHandler
    : IRequestHandler<ListTenantsQuery, Result<TenantListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ITenantRepository _tenants;
    private readonly ITenantSubscriptionRepository _subscriptions;

    public ListTenantsQueryHandler(ITenantRepository tenants, ITenantSubscriptionRepository subscriptions)
    {
        _tenants = tenants;
        _subscriptions = subscriptions;
    }

    public async Task<Result<TenantListResponseDto>> Handle(
        ListTenantsQuery request,
        CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaxPageSize);
        var skip = (page - 1) * size;

        TenantStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TenantStatus>(request.Status, ignoreCase: true, out var parsed))
                return Result<TenantListResponseDto>.Failure(
                    $"status '{request.Status}' is not a valid tenant status.");
            statusFilter = parsed;
        }

        var tenants = await _tenants.ListAsync(
            statusFilter,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            skip,
            size,
            ct);

        var total = await _tenants.CountAsync(
            statusFilter,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            ct);

        var testTenantIds = await _subscriptions.GetTestTenantIdsAsync(tenants.Select(t => t.Id).ToList(), ct);
        var items = tenants
            .Select(t => TenancyMapper.ToListItemDto(t) with { IsTest = testTenantIds.Contains(t.Id) })
            .ToList();

        return Result<TenantListResponseDto>.Success(TenancyMapper.ToListResponseDto(items, total, page, size));
    }
}
