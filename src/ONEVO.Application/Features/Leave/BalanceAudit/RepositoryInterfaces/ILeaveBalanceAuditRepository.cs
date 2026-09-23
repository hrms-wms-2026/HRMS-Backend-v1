using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

namespace ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

public interface ILeaveBalanceAuditRepository
{
    Task<IReadOnlyList<LeaveBalanceAuditRow>> ListRowsAsync(
        Guid tenantId, LeaveBalanceAuditListFilter filter, CancellationToken ct = default);
}

public record LeaveBalanceAuditListFilter(
    Guid? EmployeeId,
    Guid? LeaveTypeId,
    string? ChangeType,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    int PageSize);

public record LeaveBalanceAuditRow(
    LeaveBalanceAudit Audit,
    string EmployeeNumber,
    string EmployeeName,
    string LeaveTypeName,
    string LeaveTypeCode);
