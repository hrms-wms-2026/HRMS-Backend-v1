namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Name plus avatar only - the universal, coverage-free identity every authenticated
/// tenant employee can resolve for anyone else, for display purposes (project owner, task
/// assignee, etc.). Deliberately excludes everything EmployeeListItemResponse carries (email,
/// department, attendance status, ...), which stays behind employees:read.</summary>
public sealed record EmployeeIdentityResponse(Guid Id, string Name, Guid? AvatarFileId);
