namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record EmployeeListItemResponse(
    Guid Id,
    string EmployeeNumber,
    string FullName,
    string Email,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    string? PositionName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string EmploymentTypeLabel,
    string Status,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    /// <summary>
    /// "pending" | "accepted" | "expired" | "revoked" | null (no invitation ever issued).
    /// Only populated on the single-employee detail read (GetEmployeeQueryHandler) - always null
    /// on the list read, which does not join invitation_tokens for performance.
    /// </summary>
    string? InvitationStatus = null,
    DateTimeOffset? InvitationExpiresAt = null);
