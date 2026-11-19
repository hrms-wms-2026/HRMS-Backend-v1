namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.Models;

/// <summary>One project an employee belongs to, collapsed across the per-objective membership rows:
/// MemberSince is the earliest JoinedAt, IsActive is true while any row is still active.</summary>
public sealed record EmployeeProjectMembershipSummary(Guid ProjectId, string ProjectName, DateTimeOffset MemberSince, bool IsActive);
