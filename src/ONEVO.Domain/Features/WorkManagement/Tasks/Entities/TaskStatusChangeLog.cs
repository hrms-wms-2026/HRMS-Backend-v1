using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>Audit row for every WorkTask status move.</summary>
public class TaskStatusChangeLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid FromStatusId { get; set; }
    public Guid ToStatusId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
