using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.DeviceState.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.DeviceState.Queries.GetDeviceStateSnapshots;

public record GetDeviceStateSnapshotsQuery : IRequest<Result<PagedResult<DeviceStateSnapshotDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}
