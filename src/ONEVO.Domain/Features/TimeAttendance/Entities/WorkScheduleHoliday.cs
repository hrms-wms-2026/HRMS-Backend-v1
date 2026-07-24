using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class WorkScheduleHoliday : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkScheduleId { get; set; }
    public Guid? PublicHolidayId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>country_public_holiday | manual</summary>
    public string Source { get; set; } = "manual";

    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
