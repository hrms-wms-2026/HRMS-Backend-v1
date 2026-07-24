using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;

public record GetDailySummaryQuery(Guid EmployeeId, DateOnly Date) : IRequest<Result<ActivityDailySummaryDto>>;
