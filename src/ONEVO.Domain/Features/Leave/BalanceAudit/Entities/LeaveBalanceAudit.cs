using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

public class LeaveBalanceAudit : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public decimal DaysChanged { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Reason { get; set; }
    public Guid? RelatedRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
}
