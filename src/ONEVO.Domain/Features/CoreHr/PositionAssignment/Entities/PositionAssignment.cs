using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class PositionAssignment : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid PositionId { get; set; }
    public string AssignmentKind { get; set; } = PositionAssignmentKind.PrimaryEmployment;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string AssignmentStatus { get; set; } = PositionAssignmentStatus.Active;
    public string? ChangeReason { get; set; }
}

public static class PositionAssignmentKind
{
    public const string PrimaryEmployment = "PrimaryEmployment";
    public const string AdditionalAuthority = "AdditionalAuthority";
}

public static class PositionAssignmentStatus
{
    public const string Active = "active";
    public const string Planned = "planned";
    public const string Ended = "ended";
    public const string Cancelled = "cancelled";
}
