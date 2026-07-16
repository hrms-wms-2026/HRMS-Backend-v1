namespace ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

public sealed record ApplyRoleTemplateResultDto(
    Guid RoleId,
    string RoleName,
    IReadOnlyList<string> AppliedPermissions,
    IReadOnlyList<string> RejectedPermissions,
    IReadOnlyList<string> UniversalPermissions);
