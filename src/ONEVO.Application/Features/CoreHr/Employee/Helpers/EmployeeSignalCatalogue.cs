using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>
/// The 12 Employee Overview violations in their fixed importance rank. A signal is emitted only
/// when its source is present and its value is at least 1; the list is ordered strictly by rank.
/// </summary>
public static class EmployeeSignalCatalogue
{
    public static IReadOnlyList<EmployeeSignal> Build(EmployeeSignalInputs i)
    {
        var list = new List<EmployeeSignal>();
        void Add(string key, int rank, string sev, string cat, int value, string unit, int? denom, string label, string detail)
        {
            if (value > 0) list.Add(new(key, rank, sev, cat, value, unit, denom, label, detail));
        }
        static string S(int n, string one, string many) => n == 1 ? one : many;

        var a = i.Attendance; var d = i.Discipline; var w = i.Work; var p = i.Approvals; var m = i.Monitoring;

        if (a is not null)
        {
            Add("absent_days", 1, "critical", "attendance", a.Absent, "days", a.WorkingDays,
                $"{a.Absent} absent {S(a.Absent, "day", "days")}",
                $"No clock-in or approved leave on {a.Absent} of {a.WorkingDays} working days");
            Add("missing_clock_outs", 2, "critical", "attendance", a.MissingClockOuts, "count", a.Present,
                $"{a.MissingClockOuts} missing clock-{S(a.MissingClockOuts, "out", "outs")}", "Time entries need review");
        }
        if (d is not null)
            Add("over_break", 3, "warning", "attendance", d.OverBreakMinutes, "minutes", d.OverBreakDays,
                $"{d.OverBreakMinutes} min over break allowance", $"Exceeded on {d.OverBreakDays} {S(d.OverBreakDays, "day", "days")}");
        if (a is not null)
            Add("late_clock_ins", 4, "warning", "attendance", a.Late, "count", a.Present,
                $"{a.Late} late {S(a.Late, "arrival", "arrivals")}", $"Across {a.Present} attended {S(a.Present, "day", "days")}");
        if (d is not null)
            Add("early_clock_outs", 5, "warning", "attendance", d.EarlyClockOuts, "count", a?.Present,
                $"{d.EarlyClockOuts} early clock-{S(d.EarlyClockOuts, "out", "outs")}", "Left before scheduled end");
        if (a is not null)
            Add("short_hours_days", 6, "warning", "attendance", a.ShortHours, "days", a.Present,
                $"{a.ShortHours} short-hours {S(a.ShortHours, "day", "days")}", "Worked less than the required hours");
        if (d?.LocationViolations is int loc)
            Add("location_violations", 7, "warning", "attendance", loc, "count", null,
                $"{loc} outside-location {S(loc, "alert", "alerts")}", "Clocked in away from the work location");
        if (w is not null)
            Add("overdue_tasks", 8, "critical", "work", w.Overdue, "count", w.Assigned,
                $"{w.Overdue} overdue {S(w.Overdue, "task", "tasks")}",
                w.Assigned > 0 ? $"{w.Overdue * 100 / w.Assigned}% of assigned work overdue" : "Past due date");
        if (p is not null)
            Add("pending_approvals", 9, "info", "approvals", p.Pending, "count", null,
                $"{p.Pending} pending approval {S(p.Pending, "request", "requests")}", "Awaiting review");
        if (a is not null)
        {
            var off = a.WorkedOnNonWorkingDay + a.WorkedDuringTimeOff;
            Add("off_schedule_work", 10, "info", "attendance", off, "days", null,
                $"{off} off-schedule work {S(off, "day", "days")}", "Worked on a non-working day or during time off");
        }
        if (m is not null)
        {
            Add("idle_activity_alerts", 11, "warning", "monitoring", m.IdleAlerts, "count", null,
                $"{m.IdleAlerts} idle / low-activity {S(m.IdleAlerts, "alert", "alerts")}", "Raised by activity monitoring");
            if (m.Exceptions is int ex)
                Add("monitoring_exceptions", 12, "critical", "monitoring", ex, "count", null,
                    $"{ex} monitoring {S(ex, "exception", "exceptions")}", "Flagged cases to review");
        }

        return list.OrderBy(s => s.Rank).ToList();
    }
}
