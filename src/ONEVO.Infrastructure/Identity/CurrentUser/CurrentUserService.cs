using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    public Guid UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    // No HttpContext means this is running outside a web request (e.g. a BackgroundService that
    // has already switched into a specific tenant via ITenantContextSwitcher.SwitchToTenantAsync,
    // which sets this same scoped ITenantContext). Falling back only when HttpContext is null
    // never changes behavior on a real authenticated request.
    public Guid TenantId
    {
        get
        {
            if (_httpContextAccessor.HttpContext is null)
                return _tenantContext.TenantId;

            var value = _httpContextAccessor.HttpContext.User?.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public string Email
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public IReadOnlyList<string> Permissions
    {
        get
        {
            var claims = _httpContextAccessor.HttpContext?.User?.FindAll("permission");
            return claims?.Select(c => c.Value).ToList() ?? [];
        }
    }

    public bool HasPermission(string permission)
        => Permissions.Contains(permission);

    public bool IsAuthenticated
        => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public string? SessionBinding
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue("csrf_token_hash");

    public DateTimeOffset? SessionExpiresAt
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("session_expires_at");
            return DateTimeOffset.TryParse(value, out var expiresAt) ? expiresAt : null;
        }
    }

    public Guid? SessionId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("session_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? LegalEntityId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("legal_entity_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
