using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record AttendanceDayStatusResolution(
    string Status,
    string StatusLabel,
    string? AttentionType,
    string? AttentionLabel,
    string? AttentionSeverity,
    bool ShouldHaveClockedIn,
    int BreakOverageMinutes,
    bool IsOverBreakAllowance);

public static class AttendanceDayStatusResolver
{
    // A single continuous clock-in longer than this is treated as an unresolved missing
    // clock-out rather than a real in-progress shift. It's deliberately duration-based (not
    // tied to midnight or a calendar-day boundary) so a legitimate overnight/night-shift
    // session in progress is never flagged while it's still plausibly real work.
    public static readonly TimeSpan MissingClockOutThreshold = TimeSpan.FromHours(16);

    public static AttendanceDayStatusResolution Resolve(
        AttendanceSchedule schedule,
        string policyStatus,
        AttendanceRecord? record,
        bool hasApprovedLeave,
        bool hasOpenBreak,
        int? breakAllowanceMinutes,
        int breakUsedMinutes,
        DateTimeOffset localNow,
        DateTimeOffset now)
    {
        var isOverBreakAllowance = breakAllowanceMinutes is int allowance
            && breakUsedMinutes > allowance;
        var breakOverageMinutes = isOverBreakAllowance
            ? breakUsedMinutes - breakAllowanceMinutes!.Value
            : 0;
        var shouldHaveClockedIn = AttendanceScheduleResolver.ShouldHaveClockedIn(
            schedule, record?.ActualStart, localNow);

        if (isOverBreakAllowance)
        {
            return new AttendanceDayStatusResolution(
                AttendanceRecord.StatusOverBreak,
                "Over break allowance",
                "over_break",
                "Break time has exceeded the allowance",
                "warning",
                shouldHaveClockedIn,
                breakOverageMinutes,
                true);
        }

        if (record?.ActualStart is not null)
        {
            var isMissingClockOut = record.ActualEnd is null
                && now - record.ActualStart.Value >= MissingClockOutThreshold;
            if (isMissingClockOut)
            {
                return new AttendanceDayStatusResolution(
                    AttendanceRecord.StatusMissingClockOut,
                    "Missing clock-out",
                    "missing_clock_out",
                    "Still shown as clocked in — confirm the actual clock-out time",
                    "critical",
                    shouldHaveClockedIn,
                    0,
                    false);
            }

            if (hasApprovedLeave)
            {
                return new AttendanceDayStatusResolution(
                    AttendanceRecord.StatusWorkedDuringTimeOff,
                    "Worked during time off",
                    "worked_during_time_off",
                    "Worked during approved time off",
                    "warning",
                    shouldHaveClockedIn,
                    0,
                    false);
            }

            if (!schedule.IsWorkingDay)
            {
                return new AttendanceDayStatusResolution(
                    AttendanceRecord.StatusWorkedOnNonWorkingDay,
                    "Worked on non-working day",
                    "worked_on_non_working_day",
                    "Worked on a non-working day",
                    "warning",
                    shouldHaveClockedIn,
                    0,
                    false);
            }

            if (hasOpenBreak && record.ActualEnd is null)
            {
                return new AttendanceDayStatusResolution(
                    AttendanceRecord.StatusOnBreak,
                    "On break",
                    null,
                    null,
                    null,
                    shouldHaveClockedIn,
                    0,
                    false);
            }

            var status = record.ActualEnd is not null
                ? AttendanceRecord.StatusClockedOut
                : AttendanceRecord.StatusActive;
            return new AttendanceDayStatusResolution(
                status,
                status == AttendanceRecord.StatusClockedOut ? "Clocked out" : "Working",
                null,
                null,
                null,
                shouldHaveClockedIn,
                0,
                false);
        }

        if (hasApprovedLeave)
        {
            return new AttendanceDayStatusResolution(
                AttendanceRecord.StatusOnTimeOff,
                "On time off",
                null,
                null,
                null,
                false,
                0,
                false);
        }

        if (!schedule.IsWorkingDay)
        {
            return new AttendanceDayStatusResolution(
                AttendanceRecord.StatusNonWorkingDay,
                "Non-working day",
                null,
                null,
                null,
                false,
                0,
                false);
        }

        if (schedule.Status != "configured")
        {
            return new AttendanceDayStatusResolution(
                AttendanceRecord.StatusNoSchedule,
                "Schedule not configured",
                null,
                null,
                null,
                false,
                0,
                false);
        }

        if (policyStatus != "configured")
        {
            return new AttendanceDayStatusResolution(
                AttendanceRecord.StatusPolicyNotConfigured,
                "Clock-in policy not configured",
                null,
                null,
                null,
                false,
                0,
                false);
        }

        return new AttendanceDayStatusResolution(
            AttendanceRecord.StatusNotClockedIn,
            "Not clocked in",
            shouldHaveClockedIn ? "not_clocked_in" : null,
            shouldHaveClockedIn ? "Still has not clocked in" : null,
            shouldHaveClockedIn ? "critical" : null,
            shouldHaveClockedIn,
            0,
            false);
    }

    public static bool IsApprovedLeave(LeaveRequest request)
        => request.Status == LeaveRequestStatuses.Approved;
}
