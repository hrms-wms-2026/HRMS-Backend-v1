using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;

public record GetActivityDailySummaryQuery : IRequest<Result<ActivityDailySummaryDto?>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
}
