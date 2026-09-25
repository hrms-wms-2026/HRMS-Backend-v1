using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Filters;

/// <summary>
/// Requires at least one active tenant module without inventing an assignable module-entry
/// permission. Resource-level authority remains the responsibility of the application handler.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAnyModuleAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _moduleKeys;

    public RequireAnyModuleAttribute(params string[] moduleKeys)
        => _moduleKeys = moduleKeys;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        var entitlements = context.HttpContext.RequestServices.GetService<IModuleEntitlementService>();

        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (entitlements is null || _moduleKeys.Length == 0)
        {
            context.Result = Forbidden("An active Work Management module is required.");
            return;
        }

        var active = await entitlements.GetActiveModuleKeysForTenantAsync(
            currentUser.TenantId,
            context.HttpContext.RequestAborted);
        if (!_moduleKeys.Any(required => active.Contains(required, StringComparer.OrdinalIgnoreCase)))
            context.Result = Forbidden("An active Work Management module is required.");
    }

    private static ObjectResult Forbidden(string detail) => new(new
    {
        type = "https://onevo.com/errors/forbidden",
        title = "Forbidden",
        status = 403,
        detail
    })
    { StatusCode = StatusCodes.Status403Forbidden };
}
