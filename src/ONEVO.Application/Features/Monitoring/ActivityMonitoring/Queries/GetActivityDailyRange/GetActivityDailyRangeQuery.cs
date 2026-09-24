using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailyRange;

public record GetActivityDailyRangeQuery : IRequest<Result<List<ActivityDailySummaryDto>>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
}
