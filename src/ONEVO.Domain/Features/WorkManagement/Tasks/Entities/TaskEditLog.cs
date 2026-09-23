using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskEditLogSources
{
    public const string Direct = "direct";
    public const string ApprovedRequest = "approved_request";
}

/// <summary>
/// Audit row for every applied change to a WorkTask's editable fields. The employee is the direct editor,
/// or the requester when an edit request is approved.
/// </summary>
public class TaskEditLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Source { get; set; } = TaskEditLogSources.Direct;
    public Guid? EditRequestId { get; set; }
    public string OldValuesJson { get; set; } = "{}";
    public string NewValuesJson { get; set; } = "{}";
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
