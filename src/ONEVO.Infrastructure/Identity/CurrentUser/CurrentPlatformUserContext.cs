using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

public sealed class CurrentPlatformUserContext : ICurrentPlatformUserContext
{
    private const string AdminAuthenticationScheme = "AdminScheme";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentPlatformUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = AdminIdentity()?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(value, out var id))
                return id;
            return null;
        }
    }

    public IReadOnlyList<string> PlatformPermissions
    {
        get
        {
            var claims = AdminIdentity()?.FindAll(PlatformPermissionCatalog.PermissionClaimType);
            return claims?.Select(c => c.Value).ToList() ?? new List<string>();
        }
    }

    // TenantDatabaseTicketStore stamps the exact same ClaimTypes.NameIdentifier claim type on
    // a tenant user's own session cookie (just carrying a tenant user id, not a platform user
    // id). A scheme-blind FindFirstValue on HttpContext.User would misread any authenticated
    // tenant request's own claim as a platform admin identity. Only trust claims that came from
    // an identity ASP.NET Core actually authenticated via AdminScheme.
    private ClaimsIdentity? AdminIdentity() =>
        _httpContextAccessor.HttpContext?.User?.Identities
            .FirstOrDefault(i => i.AuthenticationType == AdminAuthenticationScheme);

    public bool HasPlatformPermission(string permissionCode)
    {
        return PlatformPermissions.Contains(permissionCode);
    }
}
