namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Requests;

public record UpdatePlatformRolePermissionsRequest(
    IReadOnlyList<string> Permissions);
