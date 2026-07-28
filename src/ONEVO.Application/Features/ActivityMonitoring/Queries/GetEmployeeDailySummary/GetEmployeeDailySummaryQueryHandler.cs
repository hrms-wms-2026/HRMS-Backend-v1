using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetEmployeeDailySummary;

public class GetEmployeeDailySummaryQueryHandler
    : IRequestHandler<GetEmployeeDailySummaryQuery, Result<EmployeeActivityDailySummaryDto?>>
{
    private readonly IActivityMonitoringRepository _repo;

    public GetEmployeeDailySummaryQueryHandler(IActivityMonitoringRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<EmployeeActivityDailySummaryDto?>> Handle(
        GetEmployeeDailySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var summary = await _repo.GetDailySummaryAsync(
            request.EmployeeId, request.Date, cancellationToken);

        if (summary is null)
            return Result<EmployeeActivityDailySummaryDto?>.NotFound(
                $"No activity summary found for {request.Date:yyyy-MM-dd}.");

        var notices = await _repo.GetConsentNoticesAsync(
            request.EmployeeId, request.Date, cancellationToken);

        var dto = new EmployeeActivityDailySummaryDto(
            summary.EmployeeId,
            summary.Date,
            summary.TotalActiveMinutes,
            summary.TotalIdleMinutes,
            summary.TotalMeetingMinutes,
            summary.ActivePercentage,
            summary.ActivityScore,
            notices
                .Select(n => new EmployeeConsentNoticeDto(n.IncidentId, n.OccurredAt, n.Decision))
                .ToList());

        return Result<EmployeeActivityDailySummaryDto?>.Success(dto);
    }
}
