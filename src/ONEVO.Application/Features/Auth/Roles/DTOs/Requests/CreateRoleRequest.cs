namespace ONEVO.Application.Features.Auth.Roles.DTOs.Requests;

public record CreateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds);
