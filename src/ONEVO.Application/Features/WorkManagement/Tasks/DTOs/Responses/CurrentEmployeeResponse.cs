namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

/// <summary>The caller's own employee id within Work Management's tenant context, resolved the same
/// way every WorkManagement command/query handler resolves it (ICallerIdentityResolver) - a
/// Work-Management-scoped stand-in for GET /api/v1/auth/me exposing this, which it does not.</summary>
public sealed record CurrentEmployeeResponse(Guid EmployeeId);
