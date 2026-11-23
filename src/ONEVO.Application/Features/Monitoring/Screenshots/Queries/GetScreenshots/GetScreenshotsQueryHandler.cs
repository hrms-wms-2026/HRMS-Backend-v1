using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.Mappers;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshots;

/// <summary>
/// monitoring:read alone is not enough: with an employeeId the caller must be able to view that
/// employee, and without one the list holds only the employees they can view (themselves plus
/// their management coverage), never the whole tenant.
/// </summary>
public class GetScreenshotsQueryHandler
    : IRequestHandler<GetScreenshotsQuery, Result<PagedResult<EvidenceAssetDto>>>
{
    private readonly IEvidenceAssetRepository _assets;
    private readonly ITenantContext _tenantContext;
    private readonly IEmployeeViewAccess _viewAccess;

    public GetScreenshotsQueryHandler(
        IEvidenceAssetRepository assets, ITenantContext tenantContext, IEmployeeViewAccess viewAccess)
    {
        _assets = assets;
        _tenantContext = tenantContext;
        _viewAccess = viewAccess;
    }

    public async Task<Result<PagedResult<EvidenceAssetDto>>> Handle(
        GetScreenshotsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<EvidenceAssetDto>>.Failure("Tenant context is required.", 401);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 200 ? 20 : request.PageSize;

        var tenantId = _tenantContext.TenantId;
        List<MonitoringEvidenceAsset> items;
        int total;
        if (request.EmployeeId is Guid employeeId)
        {
            if (!await _viewAccess.CanViewAsync(employeeId, cancellationToken))
                return Result<PagedResult<EvidenceAssetDto>>.Failure("You do not have access to this employee.", 403);

            (items, total) = await _assets.GetPagedAsync(
                tenantId, employeeId, request.From, request.To, page, pageSize, cancellationToken);
        }
        else
        {
            var viewable = await _viewAccess.ListViewableEmployeeIdsAsync(cancellationToken);
            (items, total) = viewable is null
                ? await _assets.GetPagedAsync(tenantId, null, request.From, request.To, page, pageSize, cancellationToken)
                : await _assets.GetPagedForEmployeesAsync(
                    tenantId, viewable, request.From, request.To, page, pageSize, cancellationToken);
        }

        var dtos = items.Select(EvidenceAssetMapper.ToDto).ToList();
        return Result<PagedResult<EvidenceAssetDto>>.Success(
            new PagedResult<EvidenceAssetDto>(dtos, page, pageSize, total));
    }
}
