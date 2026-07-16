namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record RoleTemplateDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> ModuleKeys,
    IReadOnlyList<string> PermissionCodes,
    bool IsSystem,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
