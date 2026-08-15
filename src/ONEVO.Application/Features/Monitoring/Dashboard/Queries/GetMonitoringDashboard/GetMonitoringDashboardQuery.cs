using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;

namespace ONEVO.Application.Features.Monitoring.Dashboard.Queries.GetMonitoringDashboard;

public sealed record GetMonitoringDashboardQuery(
    DateOnly Date,
    string? Search,
    Guid? DepartmentId,
    Guid? LegalEntityId,
    int Page = 1,
    int PageSize = 25) : IRequest<Result<MonitoringDashboardDto>>;
