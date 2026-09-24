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
    decimal EntitledHours,
    decimal AnnualHours,
    decimal CarriedForwardHours,
    decimal UsedHours,
    decimal PendingHours,
    decimal RemainingHours,
    bool IsNegative,
    DateOnly? CarryForwardExpiresOn);
