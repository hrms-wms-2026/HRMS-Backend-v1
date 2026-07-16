using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Api.Filters;

/// <summary>
/// Enforces a documented Developer Platform permission code on an admin endpoint.
/// Combine with [Authorize(Policy = "AdminPolicy")]: authentication (401) is handled by
/// the cookie scheme; this filter only turns a missing permission claim into the
/// contract 403 body ({ code, message, required_permission }). Permission claims are
/// resolved from the database on session retrieve — never from role names.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePlatformPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePlatformPermissionAttribute(string permission) => _permission = permission;

    public string Permission => _permission;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return; // [Authorize] produces the 401; nothing to check yet.

        var hasPermission = false;
        foreach (var claim in user.Claims)
        {
            if (claim.Type == PlatformPermissionCatalog.PermissionClaimType
                && string.Equals(claim.Value, _permission, StringComparison.Ordinal))
            {
                hasPermission = true;
                break;
            }
        }

        if (!hasPermission)
        {
            context.Result = new ObjectResult(new
            {
                code = "permission_denied",
                message = "You do not have permission to perform this action.",
                required_permission = _permission
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
