using System.Text.Json;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

/// <summary>
/// Resolves the legal-entity-local schedule without any persistence dependency. Both the
/// authenticated Today state and batch employee-list warning reads use this evaluator so that
/// working-day, timezone, and scheduled-start semantics cannot drift apart.
/// </summary>
public static class AttendanceScheduleResolver
{
    public static AttendanceScheduleResolution Resolve(LegalEntity legalEntity, DateTimeOffset utcNow)
    {
        var configuredTimezone = !string.IsNullOrWhiteSpace(legalEntity.Timezone);
        var timezone = configuredTimezone ? legalEntity.Timezone! : "UTC";
        var timezoneResolved = TryFindTimezone(timezone, out var zone);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
        var workDate = DateOnly.FromDateTime(localNow.DateTime);
        var configuredStart = legalEntity.WorkStartTime;
        var configuredEnd = legalEntity.WorkEndTime;
        var scheduleConfigured = configuredTimezone
            && timezoneResolved
            && configuredStart is not null
            && configuredEnd is not null
            && configuredStart.Value < configuredEnd.Value;

        if (!scheduleConfigured)
        {
            return new AttendanceScheduleResolution(
                timezone,
                zone,
                localNow,
                workDate,
                new AttendanceSchedule("not_configured", false, null, null, null));
        }

        var start = configuredStart!.Value;
        var end = configuredEnd!.Value;
        var isWorkingDay = ParseWorkingDays(legalEntity.StandardWorkingDays)
            .Contains(ToIsoDay(localNow.DayOfWeek));

        return new AttendanceScheduleResolution(
            timezone,
            zone,
            localNow,
            workDate,
            new AttendanceSchedule(
                "configured",
                isWorkingDay,
                start,
                end,
                (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes));
    }

    public static bool ShouldHaveClockedIn(
        AttendanceSchedule schedule,
        DateTimeOffset? actualStart,
        DateTimeOffset localNow)
    {
        var isAtOrAfterScheduledStart = schedule.Start is TimeOnly scheduledStart
            && localNow.TimeOfDay >= scheduledStart.ToTimeSpan();
        return schedule.IsWorkingDay
            && schedule.Status == "configured"
            && isAtOrAfterScheduledStart
            && actualStart is null;
    }

    private static bool TryFindTimezone(string timezone, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch
        {
            // Keep local-date derivation deterministic while failing closed for schedule use when
            // the configured legal-entity identifier cannot be resolved on this host.
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static HashSet<int> ParseWorkingDays(string? json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<HashSet<int>>(json ?? string.Empty);
            return values is { Count: > 0 } ? values : [1, 2, 3, 4, 5];
        }
        catch
        {
            return [1, 2, 3, 4, 5];
        }
    }

    private static int ToIsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;
}

public sealed record AttendanceScheduleResolution(
    string Timezone,
    TimeZoneInfo TimeZone,
    DateTimeOffset LocalNow,
    DateOnly WorkDate,
    AttendanceSchedule Schedule);
