using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.Mappers;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshots;

public class GetScreenshotsQueryHandler
    : IRequestHandler<GetScreenshotsQuery, Result<PagedResult<EvidenceAssetDto>>>
{
    private readonly IEvidenceAssetRepository _assets;
    private readonly ITenantContext _tenantContext;

    public GetScreenshotsQueryHandler(IEvidenceAssetRepository assets, ITenantContext tenantContext)
    {
        _assets = assets;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<EvidenceAssetDto>>> Handle(
        GetScreenshotsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<EvidenceAssetDto>>.Failure("Tenant context is required.", 401);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 200 ? 20 : request.PageSize;

        var (items, total) = await _assets.GetPagedAsync(
            _tenantContext.TenantId,
            request.EmployeeId,
            request.From,
            request.To,
            page,
            pageSize,
            cancellationToken);

        var dtos = items.Select(EvidenceAssetMapper.ToDto).ToList();
        return Result<PagedResult<EvidenceAssetDto>>.Success(
            new PagedResult<EvidenceAssetDto>(dtos, page, pageSize, total));
    }
}
