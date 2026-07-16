namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

public record ModulePermissionOwnershipDto(
    string PermissionCode,
    bool IsDefaultPermission);
