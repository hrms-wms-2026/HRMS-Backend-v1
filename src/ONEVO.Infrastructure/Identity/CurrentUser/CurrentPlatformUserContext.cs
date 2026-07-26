using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

public sealed class CurrentPlatformUserContext : ICurrentPlatformUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentPlatformUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(value, out var id))
                return id;
            return null;
        }
    }

    public IReadOnlyList<string> PlatformPermissions
    {
        get
        {
            var claims = _httpContextAccessor.HttpContext?.User?.FindAll(PlatformPermissionCatalog.PermissionClaimType);
            return claims?.Select(c => c.Value).ToList() ?? new List<string>();
        }
    }

    public bool HasPlatformPermission(string permissionCode)
    {
        return PlatformPermissions.Contains(permissionCode);
    }
}
