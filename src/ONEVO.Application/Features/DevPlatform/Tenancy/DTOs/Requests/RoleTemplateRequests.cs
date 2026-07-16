namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

public sealed record CreateRoleTemplateRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes);

public sealed record UpdateRoleTemplateRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes,
    bool IsActive);

public sealed record ApplyRoleTemplateRequest(
    string? RoleNameOverride,
    bool ForceUpdate = false);
