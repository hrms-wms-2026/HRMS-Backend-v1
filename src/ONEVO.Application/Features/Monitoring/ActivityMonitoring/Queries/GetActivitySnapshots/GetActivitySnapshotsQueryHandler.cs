using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivitySnapshots;

public class GetActivitySnapshotsQueryHandler
    : IRequestHandler<GetActivitySnapshotsQuery, Result<PagedResult<ActivitySnapshotDto>>>
{
    private readonly IActivitySnapshotRepository _snapshots;
    private readonly ITenantContext _tenantContext;

    public GetActivitySnapshotsQueryHandler(
        IActivitySnapshotRepository snapshots,
        ITenantContext tenantContext)
    {
        _snapshots = snapshots;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<ActivitySnapshotDto>>> Handle(
        GetActivitySnapshotsQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<ActivitySnapshotDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<PagedResult<ActivitySnapshotDto>>.Failure("employeeId is required.", 400);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 100 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _snapshots.GetTotalCountAsync(
            tenantId, request.EmployeeId, request.Date, cancellationToken);

        var items = await _snapshots.GetByEmployeeDateAsync(
            tenantId, request.EmployeeId, request.Date, page, pageSize, cancellationToken);

        var dtos = items.Select(s => new ActivitySnapshotDto
        {
            Id = s.Id,
            CapturedAt = s.CapturedAt,
            KeyboardEventsCount = s.KeyboardEventsCount,
            MouseEventsCount = s.MouseEventsCount,
            ActiveSeconds = s.ActiveSeconds,
            IdleSeconds = s.IdleSeconds,
            IntensityScore = s.IntensityScore,
            ForegroundProcess = s.ForegroundProcessName
        }).ToList();

        return Result<PagedResult<ActivitySnapshotDto>>.Success(
            new PagedResult<ActivitySnapshotDto>(dtos, page, pageSize, total));
    }
}
