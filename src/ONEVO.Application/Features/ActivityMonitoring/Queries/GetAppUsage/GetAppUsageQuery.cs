using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;

public record GetAppUsageQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<List<ApplicationUsageDto>>>;
