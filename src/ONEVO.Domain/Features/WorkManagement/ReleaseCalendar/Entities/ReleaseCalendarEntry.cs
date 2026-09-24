using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

public static class ReleaseReminderTypes
{
    public const string ProjectRelease = "project_release";
}

public class ReleaseCalendarEntry : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid VersionId { get; set; }
    public Guid RecipientUserId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public string ReminderType { get; set; } = ReleaseReminderTypes.ProjectRelease;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
