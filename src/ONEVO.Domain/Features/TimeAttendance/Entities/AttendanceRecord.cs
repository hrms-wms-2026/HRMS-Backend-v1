using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class AttendanceRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? WorkScheduleId { get; set; }
    public bool ExpectedWorkingDay { get; set; }
    public string? WorkTimeType { get; set; }
    public TimeOnly? ScheduledStart { get; set; }
    public TimeOnly? ScheduledEnd { get; set; }
    public int? RequiredWorkMinutes { get; set; }
    public string ExpectedWorkArea { get; set; } = "either";
    public string ScheduleTimezone { get; set; } = "UTC";
    public bool IsHoliday { get; set; }
    public string? HolidayName { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public int WorkedMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int? LateMinutes { get; set; }
    public int? ShortMinutes { get; set; }
    public string? DetectedWorkArea { get; set; }
    public string AttendanceSource { get; set; } = "agent";

    /// <summary>
    /// on_time | late | short_hours | absent | work_area_mismatch |
    /// on_time_off | holiday | off_day
    /// </summary>
    public string Status { get; set; } = "on_time";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>PostgreSQL xmin optimistic-concurrency token.</summary>
    public uint Version { get; set; }
}
