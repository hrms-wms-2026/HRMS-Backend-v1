using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

public static class ObjectiveChangeRequestTypes
{
    public const string Delete = "delete";
    public const string Edit = "edit";
    public const string Transfer = "transfer";
    public const string Achieve = "achieve";
    public const string Unachieve = "unachieve";
}

public static class ObjectiveChangeRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// One pending/decided Delete, conflicting-Edit, or Transfer request on an Objective a non-creator
/// Head cannot apply unilaterally - see docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md.
/// </summary>
public class ObjectiveChangeRequest : BaseEntity
{
    public Guid ObjectiveId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public Guid RequestedById { get; set; }
    public Guid ReportingManagerId { get; set; }
    public string Status { get; set; } = ObjectiveChangeRequestStatuses.Pending;

    /// <summary>Proposed new field values for edit/transfer requests; null for delete.</summary>
    public string? PayloadJson { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedById { get; set; }
}
