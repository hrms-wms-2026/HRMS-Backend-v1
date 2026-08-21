namespace ONEVO.Api.Contracts.Leave.Entitlements;

public record CreateManualEntitlementRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int Year,
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason);
