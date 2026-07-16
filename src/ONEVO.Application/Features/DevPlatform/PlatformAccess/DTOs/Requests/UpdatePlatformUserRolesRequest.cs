namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Requests;

public record UpdatePlatformUserRolesRequest(
    IReadOnlyList<Guid> RoleIds);
