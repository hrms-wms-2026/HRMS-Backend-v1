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
    string? ReportingManagerName);
