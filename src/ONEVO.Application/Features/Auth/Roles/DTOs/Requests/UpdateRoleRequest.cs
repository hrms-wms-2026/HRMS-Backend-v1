namespace ONEVO.Application.Features.Auth.Roles.DTOs.Requests;

/// <summary>Version: task 42, the role's row version from the last read; a stale one returns 409.</summary>
public record UpdateRoleRequest(string Name, string? Description, string? Version = null);
