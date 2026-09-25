using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyWorkPattern;

public sealed class GetMyWorkPatternQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    IActivityDailySummaryRepository summaries,
    IActivitySnapshotRepository snapshots,
    IMeetingSignalRepository meetings)
    : IRequestHandler<GetMyWorkPatternQuery, Result<WorkPatternResponse>>
{
    private const int MeetingMinutesPerSample = 2;

    public async Task<Result<WorkPatternResponse>> Handle(GetMyWorkPatternQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<WorkPatternResponse>.Forbidden();

        var tenantId = currentUser.TenantId;
        var employee = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<WorkPatternResponse>.Forbidden("No employee record for the current user.");

        var today = DateOnly.FromDateTime(dateTime.UtcNow.UtcDateTime);
        var from = request.From;
        var to = request.To;

        var pastTo = to < today ? to : today.AddDays(-1);
        var pastSummaries = from <= pastTo
            ? (await summaries.GetRangeAsync(tenantId, employee.Id, from, pastTo, ct))
                .ToDictionary(s => s.Date)
            : new Dictionary<DateOnly, ActivityDailySummary>();

        WorkPatternDayDto? todayDto = null;
        if (from <= today && today <= to)
            todayDto = await BuildTodayDtoAsync(tenantId, employee.Id, today, ct);

        var days = new List<WorkPatternDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date == today && todayDto is not null)
                days.Add(todayDto);
            else if (date > today)
                days.Add(new WorkPatternDayDto(date, 0, 0, 0, 0, 0));
            else if (pastSummaries.TryGetValue(date, out var summary))
                days.Add(ToDto(date, summary.FocusMinutes, summary.TotalMeetingMinutes, summary.TotalActiveMinutes, summary.TotalIdleMinutes, summary.ProductiveAppMinutes));
            else
                days.Add(new WorkPatternDayDto(date, 0, 0, 0, 0, 0));
        }

        return Result<WorkPatternResponse>.Success(new WorkPatternResponse(days));
    }

    private async Task<WorkPatternDayDto> BuildTodayDtoAsync(
        Guid tenantId, Guid employeeId, DateOnly today, CancellationToken ct)
    {
        var snaps = await snapshots.GetAllByEmployeeDateAsync(tenantId, employeeId, today, ct);
        var meetingSignals = await meetings.GetAllByEmployeeDateAsync(tenantId, employeeId, today, ct);

        var classified = WorkPatternWindowClassifier.Classify(snaps, meetingSignals);
        var meetingHeadlineMinutes = meetingSignals.Count(s => s.IsMeetingAppRunning) * MeetingMinutesPerSample;
        var productiveMinutes = classified.ProductiveFocusMinutes + classified.ProductiveOtherActiveMinutes
            + classified.MeetingBarMinutes; // Productive credits the OBSERVED portion of meeting time (MeetingBar),
                                             // not the headline off-device-inclusive number - see plan doc's
                                             // "EngagedTime" derivation for why.

        return new WorkPatternDayDto(
            today, classified.FocusMinutes, meetingHeadlineMinutes, classified.OtherActiveMinutes,
            classified.IdleMinutes, productiveMinutes);
    }

    private static WorkPatternDayDto ToDto(
        DateOnly date, int focusMinutes, int meetingMinutes, int activeMinutes, int idleMinutes, int productiveMinutes)
    {
        // Past-day path: ActivityDailySummary.TotalActiveMinutes/FocusMinutes/TotalMeetingMinutes/
        // ProductiveAppMinutes were already computed by the nightly job via the SAME
        // WorkPatternWindowClassifier, so this is a straight passthrough/subtraction, not a second
        // independent calculation - see WorkPatternLiveVsNightlyConsistencyTests for the proof that
        // the live and nightly paths agree.
        var otherActiveMinutes = Math.Max(0, activeMinutes - focusMinutes - meetingMinutes);
        return new WorkPatternDayDto(date, focusMinutes, meetingMinutes, otherActiveMinutes, idleMinutes, productiveMinutes);
    }
}
