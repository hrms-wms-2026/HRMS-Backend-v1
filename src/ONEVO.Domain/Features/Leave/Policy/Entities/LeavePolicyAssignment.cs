using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

/// <summary>Applies a policy to a company (legal entity), department, position or employee for the
/// policy's leave type. Dated so resolving a past day always gives the same answer: replacing or
/// removing an assignment end-dates it instead of deleting it.</summary>
public class LeavePolicyAssignment : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string TargetKind { get; set; } = Common.LeavePolicyTargetKinds.LegalEntity;
    public Guid TargetId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
    public Guid? EndedBy { get; set; }
}
