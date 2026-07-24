using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class ScheduleAssignment : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid WorkScheduleId { get; set; }

    /// <summary>full_company | department | position | employee</summary>
    public string AssignmentType { get; set; } = "full_company";

    public Guid? DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsDefaultForNewEmployee { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
