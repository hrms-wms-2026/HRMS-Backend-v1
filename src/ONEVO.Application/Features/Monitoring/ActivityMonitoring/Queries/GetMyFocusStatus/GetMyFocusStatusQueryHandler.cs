using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyFocusStatus;

public sealed class GetMyFocusStatusQueryHandler(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    IActivitySnapshotRepository snapshots)
    : IRequestHandler<GetMyFocusStatusQuery, Result<FocusStatusResponse>>
{
    /// <summary>How stale the last activity segment may be and still count as an "ongoing"
    /// streak - guards against reporting a focus streak from hours ago (e.g. after clock-out)
    /// as still in progress.</summary>
    private const int RecencyToleranceMinutes = 5;

    /// <summary>Continuous focus minutes at which the mindful-break nudge becomes due.</summary>
    private const int BreakReminderThresholdMinutes = 90;

    public async Task<Result<FocusStatusResponse>> Handle(GetMyFocusStatusQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<FocusStatusResponse>.Forbidden();

        if (currentUser.TenantId == Guid.Empty)
            return Result<FocusStatusResponse>.Forbidden("Tenant context missing.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<FocusStatusResponse>.NotFound("Current employee record was not found.");

        var now = dateTime.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var snapshotsForDay = await snapshots.GetAllByEmployeeDateAsync(
            currentUser.TenantId, employee.Id, today, ct);
        var segments = ActivityTimelineBuilder.BuildSegments(snapshotsForDay);
        var lastSegment = segments.Count > 0 ? segments[^1] : null;

        var isOngoing = lastSegment is not null
            && (now - lastSegment.EndedAt).TotalMinutes <= RecencyToleranceMinutes;

        var continuousFocusMinutes = isOngoing && lastSegment!.Type == ActivityTimelineBuilder.FocusType
            ? (int)(lastSegment.EndedAt - lastSegment.StartedAt).TotalMinutes
            : 0;

        var isBreakReminderDue = continuousFocusMinutes >= BreakReminderThresholdMinutes;

        return Result<FocusStatusResponse>.Success(
            new FocusStatusResponse(isBreakReminderDue, continuousFocusMinutes));
    }
}
