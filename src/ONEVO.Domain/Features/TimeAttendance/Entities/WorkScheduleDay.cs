using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class WorkScheduleDay : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkScheduleId { get; set; }
    public short DayOfWeek { get; set; }
    public bool IsWorkingDay { get; set; }
    public string? WorkTimeType { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public int? RequiredWorkMinutes { get; set; }
    public string? BreakType { get; set; }
    public TimeOnly? BreakStartTime { get; set; }
    public TimeOnly? BreakEndTime { get; set; }
    public int? BreakDurationMinutes { get; set; }
    public string? ExpectedWorkArea { get; set; }
    public bool IsOvernight { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
