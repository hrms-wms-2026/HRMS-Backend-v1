using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record AttendancePeriodCounts(
    int WorkingDays,
    int DaysPresent,
    int LateArrivals,
    int EarlyDepartures,
    int MissingClockOuts);

public sealed record AttendancePeriodClassification(
    int WorkingDays, int Attended, int Late, int EarlyDepartures, int MissingClockOuts,
    int Absent, int Leave, int ShortHours, int WorkedOnNonWorkingDay, int WorkedDuringTimeOff,
    IReadOnlyList<EmployeeAttendanceDay> Days);

/// <summary>
/// The late / early-departure / missing-clock-out rules behind every attendance summary. Moved
/// out of AttendanceReadHandler's monthly summary so the employee Overview cards and the
/// self-service summary can never disagree. Display-only rules: zero grace, deliberately not
/// coupled to ClockInPolicy.LateArrivalMinute payroll tiers.
/// </summary>
public static class AttendancePeriodCalculator
{
    private static readonly TimeSpan LateOrEarlyGrace = TimeSpan.Zero;

    public static TimeZoneInfo ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static AttendancePeriodCounts Count(
        IReadOnlyList<AttendanceRecord> records, TimeZoneInfo timezone, DateTimeOffset now)
    {
        var workingDays = 0;
        var daysPresent = 0;
        var late = 0;
        var early = 0;
        var missing = 0;

        foreach (var record in records)
        {
            if (record.ExpectedWorkingDay)
                workingDays += 1;

            if (record.ActualStart is null)
                continue;

            daysPresent += 1;
            if (IsLate(record, timezone)) late += 1;
            if (IsEarlyDeparture(record, timezone)) early += 1;
            if (IsMissingClockOut(record, now)) missing += 1;
        }

        return new AttendancePeriodCounts(workingDays, daysPresent, late, early, missing);
    }

    /// <summary>Classifies every date of the period against the expected-workday calendar. The
    /// single source for WorkingDays/Absent - never derive those from which records exist.
    /// Late and missing-clock-out are counted independently (as Count does); the day status
    /// shows missing_clock_out first.</summary>
    public static AttendancePeriodClassification Classify(AttendancePeriodData data)
    {
        var w = data.Workdays;
        var byDate = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        int attended = 0, late = 0, early = 0, missing = 0, absent = 0, leave = 0, shortHours = 0, offDayWork = 0, leaveWork = 0;
        var days = new List<EmployeeAttendanceDay>();

        for (var d = w.PeriodFrom; d <= w.PeriodTo; d = d.AddDays(1))
        {
            if (!w.InEffectiveRange(d)) { days.Add(new(d, "none")); continue; }

            var expected = w.ExpectedDates.Contains(d);
            var onLeave = data.ApprovedLeaves.Any(l => CoversDate(l, d));
            byDate.TryGetValue(d, out var record);

            if (record?.ActualStart is not null)
            {
                attended++;
                var isMissing = IsMissingClockOut(record, data.Now);
                var isLate = IsLate(record, data.Timezone);
                if (isMissing) missing++;
                if (isLate) late++;
                if (IsEarlyDeparture(record, data.Timezone)) early++;
                if (!expected) offDayWork++;
                else if (onLeave) leaveWork++;
                if (expected && record.ActualEnd is not null && record.RequiredWorkMinutes is int req && record.WorkedMinutes < req)
                    shortHours++;
                days.Add(new(d, isMissing ? "missing_clock_out" : isLate ? "late" : "present"));
            }
            else if (!expected) days.Add(new(d, "off"));
            else if (onLeave) { leave++; days.Add(new(d, "leave")); }
            else if (d < data.Today) { absent++; days.Add(new(d, "absent")); }
            else days.Add(new(d, "none"));
        }

        return new AttendancePeriodClassification(
            w.Count, attended, late, early, missing, absent, leave, shortHours, offDayWork, leaveWork, days);
    }

    public static string DayStatus(
        AttendanceRecord record, TimeZoneInfo timezone, DateTimeOffset now, DateOnly today, bool hasApprovedLeave)
    {
        if (record.ActualStart is not null)
        {
            if (IsMissingClockOut(record, now)) return "missing_clock_out";
            if (IsLate(record, timezone)) return "late";
            return "present";
        }

        if (hasApprovedLeave) return "leave";
        if (!record.ExpectedWorkingDay || record.IsHoliday) return "off";
        return record.Date < today ? "absent" : "none";
    }

    public static bool CoversDate(LeaveRequest leave, DateOnly date) =>
        DateOnly.FromDateTime(leave.StartAt.UtcDateTime) <= date
        && DateOnly.FromDateTime(leave.EndAt.UtcDateTime) >= date;

    private static bool IsLate(AttendanceRecord record, TimeZoneInfo timezone)
    {
        if (record.ActualStart is not DateTimeOffset actualStart || record.ScheduledStart is not TimeOnly scheduledStart)
            return false;
        var localStart = TimeZoneInfo.ConvertTime(actualStart, timezone).TimeOfDay;
        return localStart - scheduledStart.ToTimeSpan() > LateOrEarlyGrace;
    }

    private static bool IsEarlyDeparture(AttendanceRecord record, TimeZoneInfo timezone)
    {
        if (record.ActualEnd is not DateTimeOffset actualEnd || record.ScheduledEnd is not TimeOnly scheduledEnd)
            return false;
        var localEnd = TimeZoneInfo.ConvertTime(actualEnd, timezone).TimeOfDay;
        return scheduledEnd.ToTimeSpan() - localEnd > LateOrEarlyGrace;
    }

    private static bool IsMissingClockOut(AttendanceRecord record, DateTimeOffset now) =>
        record.ActualStart is DateTimeOffset start
        && record.ActualEnd is null
        && now - start >= AttendanceDayStatusResolver.MissingClockOutThreshold;
}
