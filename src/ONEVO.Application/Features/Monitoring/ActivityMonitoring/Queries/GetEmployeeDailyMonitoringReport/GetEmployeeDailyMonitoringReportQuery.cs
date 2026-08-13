using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetEmployeeDailyMonitoringReport;

public sealed record GetEmployeeDailyMonitoringReportQuery : IRequest<Result<EmployeeDailyMonitoringReportDto>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
}
