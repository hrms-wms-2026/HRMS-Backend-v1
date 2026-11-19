using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;

public sealed record GetEmployeeActivityByHourQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeActivityByHourResponse>>;
