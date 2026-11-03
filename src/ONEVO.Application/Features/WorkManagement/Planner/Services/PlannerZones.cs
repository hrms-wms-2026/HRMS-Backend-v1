using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>An employee's planning zone, shared by the day planner and the team planner.</summary>
internal static class PlannerZones
{
    internal readonly record struct Zone(string Id, TimeZoneInfo Info);

    /// <summary>Null when the employee does not exist in the tenant.</summary>
    internal static async Task<Zone?> ResolveAsync(IEmployeeRepository employees, ICalendarTimezoneResolver timezones, Guid tenantId,
        Guid employeeId, CancellationToken ct)
    {
        var employee = (await employees.ListByIdsAsync(tenantId, [employeeId], ct)).GetValueOrDefault(employeeId);
        return employee is null ? null : await ResolveForAsync(timezones, tenantId, employee, ct);
    }

    /// <summary>The zone of an employee already loaded.</summary>
    internal static async Task<Zone> ResolveForAsync(ICalendarTimezoneResolver timezones, Guid tenantId, Employee employee, CancellationToken ct)
    {
        var id = await timezones.ResolveForUserAsync(tenantId, employee.UserId, ct);
        // A zone id this machine does not know (a typo in the profile or legal entity) plans in UTC instead of failing.
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var info) ? new Zone(id, info) : new Zone("UTC", TimeZoneInfo.Utc);
    }

    internal static DateOnly TodayIn(DateTimeOffset utcNow, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);

    internal static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>A wall-clock time on <paramref name="day"/> in <paramref name="zone"/>, as an instant.</summary>
    internal static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
