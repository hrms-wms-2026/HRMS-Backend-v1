namespace ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

public record LeaveEntitlementResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    int Year,
    decimal TotalHours,
    decimal CarriedForwardHours,
    decimal UsedHours,
    decimal PendingHours,
    decimal RemainingHours,
    string Source,
    string? ManualReason,
    bool IsOverUtilized,
    string? Warning,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record LeaveEntitlementGenerationPreviewResponse(
    int Year,
    int EmployeeCount,
    int EntitlementLineCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Lines,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped);

public record LeaveEntitlementGenerationResultResponse(
    int Year,
    int CreatedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Created,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped,
    IReadOnlyList<LeaveEntitlementGenerationErrorResponse> Errors);

public record LeaveEntitlementGenerationLineResponse(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    decimal TotalHours,
    decimal CarriedForwardHours,
    decimal RemainingHours,
    bool ProbationRestrictionApplied,
    decimal ForfeitedHours,
    DateOnly? CarryForwardExpiresOn,
    string? Warning);

public record LeaveEntitlementGenerationSkipResponse(
    Guid? EmployeeId,
    string? EmployeeName,
    string Reason);

public record LeaveEntitlementGenerationErrorResponse(
    Guid? EmployeeId,
    string? EmployeeName,
    string Reason);
