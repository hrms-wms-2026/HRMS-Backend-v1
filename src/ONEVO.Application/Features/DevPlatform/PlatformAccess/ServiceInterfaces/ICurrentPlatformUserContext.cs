namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

public interface ICurrentPlatformUserContext
{
    Guid? UserId { get; }
    IReadOnlyList<string> PlatformPermissions { get; }
    bool HasPlatformPermission(string permissionCode);
}
