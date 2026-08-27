namespace ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

public record LeaveBalanceResponse(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    int Year,
    decimal EntitledDays,
    decimal AnnualDays,
    decimal CarriedForwardDays,
    decimal UsedDays,
    decimal PendingDays,
    decimal RemainingDays,
    bool IsNegative,
    DateOnly? CarryForwardExpiresOn);
