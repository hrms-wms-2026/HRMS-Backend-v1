using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Queries.GetAppUsageSnapshots;

public class GetAppUsageSnapshotsQueryHandler
    : IRequestHandler<GetAppUsageSnapshotsQuery, Result<PagedResult<AppUsageSnapshotDto>>>
{
    private readonly IAppUsageSnapshotRepository _snapshots;
    private readonly ITenantContext _tenantContext;

    public GetAppUsageSnapshotsQueryHandler(IAppUsageSnapshotRepository snapshots, ITenantContext tenantContext)
    {
        _snapshots = snapshots;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<AppUsageSnapshotDto>>> Handle(
        GetAppUsageSnapshotsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<AppUsageSnapshotDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<PagedResult<AppUsageSnapshotDto>>.Failure("employeeId is required.", 400);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 100 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _snapshots.GetTotalCountAsync(tenantId, request.EmployeeId, request.Date, ct);
        var items = await _snapshots.GetByEmployeeDateAsync(tenantId, request.EmployeeId, request.Date, page, pageSize, ct);

        var dtos = items.Select(s => new AppUsageSnapshotDto
        {
            Id = s.Id,
            CapturedAt = s.CapturedAt,
            ProcessName = s.ProcessName,
            WindowTitleHash = s.WindowTitleHash
        }).ToList();

        return Result<PagedResult<AppUsageSnapshotDto>>.Success(
            new PagedResult<AppUsageSnapshotDto>(dtos, page, pageSize, total));
    }
}
