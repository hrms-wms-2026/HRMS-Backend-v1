using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetEmployeeDailySummary;

public record GetEmployeeDailySummaryQuery(
    Guid EmployeeId,
    DateOnly Date) : IRequest<Result<EmployeeActivityDailySummaryDto?>>;
