using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

/// <summary>Stable identity of a policy for exactly one leave type. Every rule lives on its
/// dated <see cref="LeavePolicyVersion"/> rows; who it applies to lives on
/// <see cref="LeavePolicyAssignment"/> rows.</summary>
public class LeavePolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
