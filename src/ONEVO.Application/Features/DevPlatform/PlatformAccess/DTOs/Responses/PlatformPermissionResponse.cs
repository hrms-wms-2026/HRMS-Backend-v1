namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;

public record PlatformPermissionResponse(
    string Code,
    string ModuleKey,
    string Description,
    bool IsHighRisk);
