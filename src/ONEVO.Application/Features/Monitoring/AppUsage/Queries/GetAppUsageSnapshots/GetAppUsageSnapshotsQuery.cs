using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.AppUsage.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.AppUsage.Queries.GetAppUsageSnapshots;

public record GetAppUsageSnapshotsQuery : IRequest<Result<PagedResult<AppUsageSnapshotDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
}
