using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

public class LeaveBalanceAudit : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public decimal HoursChanged { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Reason { get; set; }
    public Guid? RelatedRequestId { get; set; }

    /// <summary>Policy credits only: the policy/version that produced the hours, the leave year, and
    /// the months the hours are spread over (month-attributed rows are what crediting counts as
    /// already credited; every other row is left alone).</summary>
    public Guid? LeavePolicyId { get; set; }
    public Guid? LeavePolicyVersionId { get; set; }
    public int? Year { get; set; }
    public int? CoversFromMonth { get; set; }
    public int? CoversToMonth { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
}
