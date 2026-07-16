using System.Security.Claims;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Api.Middleware;

public class PermissionVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PermissionVersionMiddleware> _logger;

    public PermissionVersionMiddleware(RequestDelegate next, ILogger<PermissionVersionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IPermissionVersionService permissionVersionService)
    {
        if (!ShouldValidate(context))
        {
            await _next(context);
            return;
        }

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            await RejectAsync(context, "Authenticated request is missing a valid user identifier.");
            return;
        }

        var tokenVersionValue = context.User.FindFirstValue("perm_ver");
        if (!long.TryParse(tokenVersionValue, out var tokenVersion))
        {
            await RejectAsync(context, "Authenticated request is missing a permission version.");
            return;
        }

        try
        {
            var currentVersion = await permissionVersionService.GetCurrentVersionAsync(userId, context.RequestAborted);
            if (tokenVersion < currentVersion)
            {
                _logger.LogInformation(
                    "Rejected stale permission version for user {UserId}. Token={TokenVersion}, Current={CurrentVersion}",
                    userId,
                    tokenVersion,
                    currentVersion);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://onevo.com/errors/permission-version-stale",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Session permissions are stale. Refresh the session."
                });
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permission version validation failed open for user {UserId}", userId);
        }

        await _next(context);
    }

    private static bool ShouldValidate(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1"))
            return false;

        var identity = context.User.Identity;
        if (identity?.IsAuthenticated != true)
            return false;

        // Tenant cookie tickets are rebuilt by TenantDatabaseTicketStore on every
        // request, including current permissions. Permission-version claims apply
        // only to non-browser token contracts that carry a perm_ver claim.
        return !string.Equals(
            identity.AuthenticationType,
            "TenantScheme",
            StringComparison.Ordinal);
    }

    private static async Task RejectAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://onevo.com/errors/invalid-session",
            title = "Unauthorized",
            status = 401,
            detail
        });
    }
}
