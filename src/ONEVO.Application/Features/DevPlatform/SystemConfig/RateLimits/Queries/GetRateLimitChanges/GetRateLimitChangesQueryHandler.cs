using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitChanges;

public sealed class GetRateLimitChangesQueryHandler
    : IRequestHandler<GetRateLimitChangesQuery, Result<PagedResult<RateLimitChangeDto>>>
{
    private readonly IRateLimitSettingRepository _repository;

    public GetRateLimitChangesQueryHandler(IRateLimitSettingRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<RateLimitChangeDto>>> Handle(GetRateLimitChangesQuery request, CancellationToken ct)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim();
        var page = await _repository.ListChangesAsync(
            new RateLimitChangeFilter(scope, request.TenantId, request.SubscriptionPlanId),
            request.Page,
            request.PageSize,
            ct);
        return Result<PagedResult<RateLimitChangeDto>>.Success(page);
    }
}
