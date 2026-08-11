using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

/// <summary>
/// Materialized reporting-tree cache derived from positions.reports_to_position_id and
/// active PrimaryEmployment position_assignments. Not source of truth: safe to delete and
/// rebuild, never edited directly by application code outside the rebuild service.
/// </summary>
public class EmployeeHierarchyClosure : ITenantOwnedEntity
{
    public Guid TenantId { get; set; }
    public Guid AncestorEmployeeId { get; set; }
    public Guid DescendantEmployeeId { get; set; }
    public int Depth { get; set; }
    public Guid SourcePositionAssignmentId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}
