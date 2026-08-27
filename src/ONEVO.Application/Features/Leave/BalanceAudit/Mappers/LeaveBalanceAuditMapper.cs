using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Mappers;

public static class LeaveBalanceAuditMapper
{
    public static LeaveBalanceAuditResponse ToResponse(LeaveBalanceAuditRow row) => new(
        row.Audit.Id, row.Audit.EmployeeId, row.EmployeeNumber, row.EmployeeName,
        row.Audit.LeaveTypeId, row.LeaveTypeName, row.LeaveTypeCode,
        row.Audit.ChangeType, row.Audit.DaysChanged, row.Audit.BalanceAfter,
        row.Audit.Reason, row.Audit.RelatedRequestId, row.Audit.CreatedAt);
}
