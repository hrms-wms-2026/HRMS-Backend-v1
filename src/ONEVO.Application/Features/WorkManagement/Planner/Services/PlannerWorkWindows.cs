using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>
/// The work window the planner shades and loads against (2026-10-27 Planner = Calendar design, section 4.5). It uses the
/// same "is a schedule configured" and working-day rules as clock-in (<see cref="AttendanceScheduleResolver"/>), so an
/// overnight or half-set schedule is "not set" here too (call C5).
/// </summary>
public static class PlannerWorkWindows
{
    private const int BreakSnapMinutes = 15;

    public static PlannerWorkWindow? For(LegalEntity? legalEntity, DateOnly day)
    {
        if (legalEntity is null || !AttendanceScheduleResolver.IsScheduleConfigured(legalEntity))
            return null;

        var start = legalEntity.WorkStartTime!.Value;
        var end = legalEntity.WorkEndTime!.Value;
        var span = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        var breakMinutes = Math.Clamp(legalEntity.BreakDurationMinutes ?? 0, 0, span);
        var isWorkingDay = AttendanceScheduleResolver.ParseWorkingDays(legalEntity.StandardWorkingDays)
            .Contains(day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek);

        TimeOnly? breakStart = null;
        if (breakMinutes > 0)
        {
            var offset = (span - breakMinutes) / 2 / BreakSnapMinutes * BreakSnapMinutes;
            breakStart = start.AddMinutes(offset);
        }

        return new PlannerWorkWindow(start, end, breakMinutes, breakStart, isWorkingDay,
            isWorkingDay ? span - breakMinutes : 0);
    }
}
