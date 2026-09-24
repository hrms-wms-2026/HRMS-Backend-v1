using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Queries.GetDeviceStateSnapshots;

public class GetDeviceStateSnapshotsQueryHandler
    : IRequestHandler<GetDeviceStateSnapshotsQuery, Result<PagedResult<DeviceStateSnapshotDto>>>
{
    private readonly IDeviceStateSnapshotRepository _snapshots;
    private readonly ITenantContext _tenantContext;

    public GetDeviceStateSnapshotsQueryHandler(IDeviceStateSnapshotRepository snapshots, ITenantContext tenantContext)
    {
        _snapshots = snapshots;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<DeviceStateSnapshotDto>>> Handle(
        GetDeviceStateSnapshotsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<DeviceStateSnapshotDto>>.Failure("Tenant context is required.", 401);

        if (request.EmployeeId == Guid.Empty)
            return Result<PagedResult<DeviceStateSnapshotDto>>.Failure("employeeId is required.", 400);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 500 ? 100 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _snapshots.GetTotalCountAsync(tenantId, request.EmployeeId, request.Date, ct);
        var items = await _snapshots.GetByEmployeeDateAsync(tenantId, request.EmployeeId, request.Date, page, pageSize, ct);

        var dtos = items.Select(s => new DeviceStateSnapshotDto
        {
            Id = s.Id,
            CapturedAt = s.CapturedAt,
            IdleSeconds = s.IdleSeconds,
            IsIdle = s.IsIdle
        }).ToList();

        return Result<PagedResult<DeviceStateSnapshotDto>>.Success(
            new PagedResult<DeviceStateSnapshotDto>(dtos, page, pageSize, total));
    }
}
