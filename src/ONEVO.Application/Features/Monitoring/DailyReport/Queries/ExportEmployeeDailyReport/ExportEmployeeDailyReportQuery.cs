using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.DailyReport.Queries.ExportEmployeeDailyReport;

public record ExportEmployeeDailyReportQuery : IRequest<Result<DailyReportExportFile>>
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
}
