using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;

/// <summary>One person the availability rules look at. <paramref name="Zone"/> is that person's own effective
/// timezone; <paramref name="LeaveVisible"/> says whether the caller may see this person's leave.</summary>
public sealed record AvailabilityParticipant(
    Guid EmployeeId, string EmployeeName, TimeZoneInfo Zone, LegalEntity? LegalEntity, bool LeaveVisible);

/// <summary>
/// The leave, holiday and working-hours warnings for one person and one meeting time (meeting availability
/// warnings, 2026-10-28 design 4.2). Pure: the caller loads the data. Every row is a warning, never a block.
/// </summary>
public static class ParticipantAvailabilityRules
{
    public static IReadOnlyList<CalendarConflict> Evaluate(
        AvailabilityParticipant p, DateTimeOffset start, DateTimeOffset end, bool isAllDay,
        IReadOnlyList<LeaveRequest> approvedLeave, IReadOnlyList<LeaveCalendarHoliday> holidays)
    {
        var rows = new List<CalendarConflict>();
        if (end <= start)
            return rows;

        foreach (var leave in approvedLeave.Where(l => l.EmployeeId == p.EmployeeId).OrderBy(l => l.StartAt))
        {
            var leaveEnd = leave.PartialCancelEffectiveDate is DateOnly cancelFrom
                ? Min(leave.EndAt, DayStart(cancelFrom, p.Zone))
                : leave.EndAt;
            if (leave.StartAt < end && leaveEnd > start)
                rows.Add(Row(p, p.LeaveVisible ? "On leave" : "Busy", Max(start, leave.StartAt), Min(end, leaveEnd),
                    CalendarConflictKinds.Leave));
        }

        var dates = LocalDates(start, end, p.Zone);

        if (p.LegalEntity is not null)
        {
            var entityHolidays = holidays
                .Where(h => h.LegalEntityId is null || h.LegalEntityId == p.LegalEntity.Id)
                .GroupBy(h => h.Date)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var date in dates)
            {
                if (entityHolidays.TryGetValue(date, out var holiday))
                    rows.Add(Row(p, $"Public holiday: {holiday.Name}", Max(start, DayStart(date, p.Zone)),
                        Min(end, DayStart(date.AddDays(1), p.Zone)), CalendarConflictKinds.Holiday));
            }
        }

        if (!isAllDay)
        {
            foreach (var date in dates)
            {
                var window = PlannerWorkWindows.For(p.LegalEntity, date);
                if (window is null)
                    continue;
                var segStart = Max(start, DayStart(date, p.Zone));
                var segEnd = Min(end, DayStart(date.AddDays(1), p.Zone));
                if (!window.IsWorkingDay)
                {
                    rows.Add(Row(p, "Non-working day", segStart, segEnd, CalendarConflictKinds.OutsideHours));
                    continue;
                }

                var windowStart = Local(date, window.WorkStart, p.Zone);
                var windowEnd = Local(date, window.WorkEnd, p.Zone);
                DateTimeOffset? outsideStart = null, outsideEnd = null;
                if (segStart < windowStart)
                    (outsideStart, outsideEnd) = (segStart, Min(segEnd, windowStart));
                if (segEnd > windowEnd)
                    (outsideStart, outsideEnd) = (outsideStart ?? Max(segStart, windowEnd), segEnd);
                if (outsideStart is not null)
                    rows.Add(Row(p, $"Outside working hours ({window.WorkStart:HH\\:mm}–{window.WorkEnd:HH\\:mm})",
                        outsideStart.Value, outsideEnd!.Value, CalendarConflictKinds.OutsideHours));
            }
        }

        return rows;
    }

    private static CalendarConflict Row(
        AvailabilityParticipant p, string title, DateTimeOffset from, DateTimeOffset to, string kind) =>
        new(p.EmployeeId, p.EmployeeName, Guid.Empty, title, from, to, Kind: kind);

    /// <summary>The local dates the half-open range [start, end) touches in <paramref name="zone"/>.</summary>
    private static List<DateOnly> LocalDates(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end.AddTicks(-1), zone).DateTime);
        var dates = new List<DateOnly>();
        for (var d = first; d <= last; d = d.AddDays(1))
            dates.Add(d);
        return dates;
    }

    private static DateTimeOffset DayStart(DateOnly date, TimeZoneInfo zone) => Local(date, TimeOnly.MinValue, zone);

    private static DateTimeOffset Local(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
