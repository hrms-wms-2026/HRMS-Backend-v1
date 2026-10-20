using ONEVO.Domain.Features.TimeAttendance.Alerts;
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

public sealed record AttendanceViolationDates(
    IReadOnlyList<DateOnly> Absent,
    IReadOnlyList<DateOnly> Late,
    IReadOnlyList<DateOnly> EarlyDepartures,
    IReadOnlyList<DateOnly> MissingClockOuts,
    IReadOnlyList<DateOnly> ShortHours,
    IReadOnlyList<DateOnly> WorkedOnNonWorkingDay,
    IReadOnlyList<DateOnly> WorkedDuringTimeOff);

public sealed record AttendancePeriodClassification(
    int WorkingDays, int Attended, int Late, int EarlyDepartures, int MissingClockOuts,
    int Absent, int Leave, int ShortHours, int WorkedOnNonWorkingDay, int WorkedDuringTimeOff,
    IReadOnlyList<EmployeeAttendanceDay> Days,
    AttendanceViolationDates Dates);

public sealed record OverBreakDay(DateOnly Date, int MinutesOver);

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
        IReadOnlyList<AttendanceRecord> records, TimeZoneInfo timezone, DateTimeOffset now, int thresholdMinutes = 0)
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
            if (IsLate(record, timezone, thresholdMinutes)) late += 1;
            if (IsEarlyDeparture(record, timezone, thresholdMinutes)) early += 1;
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
        int attended = 0, leave = 0;
        var absent = new List<DateOnly>();
        var late = new List<DateOnly>();
        var early = new List<DateOnly>();
        var missing = new List<DateOnly>();
        // Short hours is retired (late, early, over-break and time off cover missing time); kept empty for callers.
        var shortHours = new List<DateOnly>();
        var offDayWork = new List<DateOnly>();
        var leaveWork = new List<DateOnly>();
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
                var (isLate, isEarly) = LateEarly(record, data);
                if (isMissing) missing.Add(d);
                if (isLate) late.Add(d);
                if (isEarly) early.Add(d);
                if (!expected) offDayWork.Add(d);
                else if (onLeave) leaveWork.Add(d);
                days.Add(new(d, isMissing ? "missing_clock_out" : isLate ? "late" : "present"));
            }
            else if (!expected) days.Add(new(d, "off"));
            else if (onLeave) { leave++; days.Add(new(d, "leave")); }
            else if (d < data.Today) { absent.Add(d); days.Add(new(d, "absent")); }
            else days.Add(new(d, "none"));
        }

        // Alerts a reviewer excused (or converted to leave) stop counting; the day list still shows what happened.
        absent = Keep(absent, data, AttendanceAlertSignals.AbsentDays);
        late = Keep(late, data, AttendanceAlertSignals.LateClockIns);
        early = Keep(early, data, AttendanceAlertSignals.EarlyClockOuts);
        missing = Keep(missing, data, AttendanceAlertSignals.MissingClockOuts);
        offDayWork = Keep(offDayWork, data, AttendanceAlertSignals.OffScheduleWork);

        return new AttendancePeriodClassification(
            w.Count, attended, late.Count, early.Count, missing.Count, absent.Count, leave,
            shortHours.Count, offDayWork.Count, leaveWork.Count, days,
            new AttendanceViolationDates(absent, late, early, missing, shortHours, offDayWork, leaveWork));
    }

    private static List<DateOnly> Keep(List<DateOnly> dates, AttendancePeriodData data, string signal) =>
        data.Excused is not null && data.Excused.TryGetValue(signal, out var excused) && excused.Count > 0
            ? dates.Where(d => !excused.Contains(d)).ToList()
            : dates;

    /// <summary>
    /// The Attendance card's per-day list: Classify's status for every day up to today (newest first),
    /// joined with that day's record for clock times, hours, breaks and late/early minutes. Days
    /// outside the employment range or still ahead are left out.
    /// </summary>
    public static IReadOnlyList<EmployeeAttendanceDayDetail> DayDetails(AttendancePeriodData data)
    {
        var classification = Classify(data);
        var byDate = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        var overBreak = OverBreakDays(data).ToDictionary(o => o.Date, o => o.MinutesOver);

        return classification.Days
            .Where(day => day.Status != "none" && day.Date <= data.Today)
            .OrderByDescending(day => day.Date)
            .Select(day =>
            {
                byDate.TryGetValue(day.Date, out var record);
                var expected = data.Workdays.ExpectedDates.Contains(day.Date);
                var clockIn = record?.ActualStart;
                var clockOut = record?.ActualEnd;
                var breakMinutes = data.BreakMinutesByDate.TryGetValue(day.Date, out var used) ? used : record?.BreakMinutes ?? 0;
                var (isLate, isEarly) = record is null ? (false, false) : LateEarly(record, data);
                var (lateMinutes, earlyMinutes) = record is null ? (0, 0) : MissingMinutes(record, data);
                return new EmployeeAttendanceDayDetail(
                    day.Date,
                    day.Status,
                    expected,
                    record?.IsHoliday == true ? record.HolidayName : null,
                    record?.ScheduledStart?.ToString("HH:mm"),
                    record?.ScheduledEnd?.ToString("HH:mm"),
                    clockIn,
                    clockOut,
                    clockIn is { } i ? TimeZoneInfo.ConvertTime(i, data.Timezone).ToString("HH:mm") : null,
                    clockOut is { } o ? TimeZoneInfo.ConvertTime(o, data.Timezone).ToString("HH:mm") : null,
                    record?.WorkedMinutes ?? 0,
                    record?.RequiredWorkMinutes,
                    breakMinutes,
                    isLate ? lateMinutes : 0,
                    isEarly ? earlyMinutes : 0,
                    false,
                    overBreak.TryGetValue(day.Date, out var over) ? over : 0,
                    record?.ExpectedWorkModeName);
            })
            .ToList();
    }

    /// <summary>Whole minutes missing at each end of the day: after the scheduled start and before the scheduled end,
    /// where approved time off moves those edges (start to the end of leave covering it, end to the start of leave
    /// covering it). A day fully covered by leave, an unscheduled day or a missing clock-out (for early) is 0.</summary>
    public static (int Late, int Early) MissingMinutes(AttendanceRecord record, AttendancePeriodData data)
    {
        var (late, early) = MissingSpans(record, data);
        return ((int)Math.Floor(late.TotalMinutes), (int)Math.Floor(early.TotalMinutes));
    }

    /// <summary>Late/early under the policy: from the threshold minutes when there is one, otherwise any lateness.</summary>
    private static (bool Late, bool Early) LateEarly(AttendanceRecord record, AttendancePeriodData data)
    {
        var (late, early) = MissingSpans(record, data);
        return (Counts(late, data.LateEarlyThresholdMinutes), Counts(early, data.LateEarlyThresholdMinutes));
    }

    private static bool Counts(TimeSpan missing, int thresholdMinutes) =>
        thresholdMinutes > 0 ? Math.Floor(missing.TotalMinutes) >= thresholdMinutes : missing > LateOrEarlyGrace;

    private static (TimeSpan Late, TimeSpan Early) MissingSpans(AttendanceRecord record, AttendancePeriodData data)
    {
        if (record.ScheduledStart is not TimeOnly scheduledStart || record.ScheduledEnd is not TimeOnly scheduledEnd)
            return (TimeSpan.Zero, TimeSpan.Zero);
        var start = LocalInstant(record.Date, scheduledStart, data.Timezone);
        var end = LocalInstant(record.Date, scheduledEnd, data.Timezone);

        // Leaves may chain (09:00-10:00 then 10:00-11:00), so keep moving the edges until they settle.
        for (var moved = true; moved && start < end;)
        {
            moved = false;
            foreach (var leave in data.ApprovedLeaves)
            {
                if (leave.StartAt <= start && leave.EndAt > start) { start = leave.EndAt; moved = true; }
                if (leave.StartAt < end && leave.EndAt >= end) { end = leave.StartAt; moved = true; }
            }
        }
        if (start >= end)
            return (TimeSpan.Zero, TimeSpan.Zero);

        var late = record.ActualStart is DateTimeOffset actualStart && actualStart > start ? actualStart - start : TimeSpan.Zero;
        var early = record.ActualEnd is DateTimeOffset actualEnd && actualEnd < end ? end - actualEnd : TimeSpan.Zero;
        return (late, early);
    }

    private static DateTimeOffset LocalInstant(DateOnly date, TimeOnly time, TimeZoneInfo timezone)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, timezone.GetUtcOffset(local));
    }

    /// <summary>Days whose total break exceeded the configured allowance, with the minutes over.
    /// The single over-break rule: the discipline counts and the drill-down list both use it.</summary>
    public static IReadOnlyList<OverBreakDay> OverBreakDays(AttendancePeriodData data)
    {
        if (data.BreakAllowanceMinutes is not int allowance)
            return Array.Empty<OverBreakDay>();

        return data.Records
            .Where(r => data.BreakMinutesByDate.TryGetValue(r.Date, out var used) && used > allowance)
            .Select(r => new OverBreakDay(r.Date, data.BreakMinutesByDate[r.Date] - allowance))
            .OrderBy(o => o.Date)
            .ToList();
    }

    public static string DayStatus(
        AttendanceRecord record, TimeZoneInfo timezone, DateTimeOffset now, DateOnly today, bool hasApprovedLeave, int thresholdMinutes = 0)
    {
        if (record.ActualStart is not null)
        {
            if (IsMissingClockOut(record, now)) return "missing_clock_out";
            if (IsLate(record, timezone, thresholdMinutes)) return "late";
            return "present";
        }

        if (hasApprovedLeave) return "leave";
        if (!record.ExpectedWorkingDay || record.IsHoliday) return "off";
        return record.Date < today ? "absent" : "none";
    }

    public static bool CoversDate(LeaveRequest leave, DateOnly date) =>
        DateOnly.FromDateTime(leave.StartAt.UtcDateTime) <= date
        && DateOnly.FromDateTime(leave.EndAt.UtcDateTime) >= date;

    // Leave-unaware edges, for the record-only callers (Count, DayStatus) that have no period data.
    private static bool IsLate(AttendanceRecord record, TimeZoneInfo timezone, int thresholdMinutes)
    {
        if (record.ActualStart is not DateTimeOffset actualStart || record.ScheduledStart is not TimeOnly scheduledStart)
            return false;
        var localStart = TimeZoneInfo.ConvertTime(actualStart, timezone).TimeOfDay;
        return Counts(localStart - scheduledStart.ToTimeSpan(), thresholdMinutes);
    }

    private static bool IsEarlyDeparture(AttendanceRecord record, TimeZoneInfo timezone, int thresholdMinutes)
    {
        if (record.ActualEnd is not DateTimeOffset actualEnd || record.ScheduledEnd is not TimeOnly scheduledEnd)
            return false;
        var localEnd = TimeZoneInfo.ConvertTime(actualEnd, timezone).TimeOfDay;
        return Counts(scheduledEnd.ToTimeSpan() - localEnd, thresholdMinutes);
    }

    private static bool IsMissingClockOut(AttendanceRecord record, DateTimeOffset now) =>
        record.ActualStart is DateTimeOffset start
        && record.ActualEnd is null
        && now - start >= AttendanceDayStatusResolver.MissingClockOutThreshold;
}
