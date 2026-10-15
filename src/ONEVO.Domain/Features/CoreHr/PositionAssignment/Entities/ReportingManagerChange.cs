using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>
/// Immutable history for a direct reporting-manager change. This is separate from position
/// assignment history because changing a manager is not a position transfer.
/// </summary>
public sealed class ReportingManagerChange : BaseEntity
{
    public Guid BatchId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? PreviousManagerEmployeeId { get; set; }
    public Guid NewManagerEmployeeId { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string? ChangeReason { get; set; }
}
