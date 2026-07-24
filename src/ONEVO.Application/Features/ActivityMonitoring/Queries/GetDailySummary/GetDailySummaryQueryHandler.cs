using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;

namespace ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;

public class GetDailySummaryQueryHandler
    : IRequestHandler<GetDailySummaryQuery, Result<ActivityDailySummaryDto>>
{
    private readonly IActivityMonitoringRepository _repo;
    public GetDailySummaryQueryHandler(IActivityMonitoringRepository repo) => _repo = repo;

    public async Task<Result<ActivityDailySummaryDto>> Handle(
        GetDailySummaryQuery request, CancellationToken ct)
    {
        var summary = await _repo.GetDailySummaryAsync(request.EmployeeId, request.Date, ct);
        if (summary is null)
            return Result<ActivityDailySummaryDto>.NotFound("No summary found for this employee and date.");

        return Result<ActivityDailySummaryDto>.Success(new ActivityDailySummaryDto(
            summary.EmployeeId, summary.Date,
            summary.TotalActiveMinutes, summary.TotalIdleMinutes, summary.TotalMeetingMinutes,
            summary.ActivePercentage, summary.ProductiveAppMinutes, summary.PersonalAppMinutes,
            summary.ActivityScore, summary.TopAppsJson, summary.IntensityAvg,
            summary.KeyboardTotal, summary.MouseTotal));
    }
}
