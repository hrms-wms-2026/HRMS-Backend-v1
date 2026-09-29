using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>A user's unsaved "create task" form. Private to its owner; the payload is the
/// frontend's form snapshot and is opaque to the backend.</summary>
public class TaskDraft : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}
