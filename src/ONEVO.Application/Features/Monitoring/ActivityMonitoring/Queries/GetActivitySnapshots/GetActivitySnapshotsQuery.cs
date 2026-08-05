using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivitySnapshots;

public record GetActivitySnapshotsQuery : IRequest<Result<PagedResult<ActivitySnapshotDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}
