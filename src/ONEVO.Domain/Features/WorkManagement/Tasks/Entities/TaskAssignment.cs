namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>Who is assigned to a task. HR-availability-check enrichment deferred - see plan Global Constraints.</summary>
public class TaskAssignment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AssignedById { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}
