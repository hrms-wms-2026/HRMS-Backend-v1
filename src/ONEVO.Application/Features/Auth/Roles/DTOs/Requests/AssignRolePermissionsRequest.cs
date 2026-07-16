namespace ONEVO.Application.Features.Auth.Roles.DTOs.Requests;

public record AssignRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);
