using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;

public sealed record GetEmployeeActivityOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeActivityOverviewResponse>>;
