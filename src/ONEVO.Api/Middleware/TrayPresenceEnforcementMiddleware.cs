using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Options;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Api.Middleware;

public sealed class TrayPresenceEnforcementMiddleware
{
    private const string TrayDeviceTokenType = "tray_device";
    private const string ActiveTrayRequiredType = "https://onevo.com/errors/active-tray-required";
    private readonly RequestDelegate _next;

    public TrayPresenceEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ILogger<TrayPresenceEnforcementMiddleware> logger)
    {
        var endpoint = context.GetEndpoint();
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        var options = context.RequestServices.GetRequiredService<IOptions<TrayPresenceOptions>>().Value;
        var principal = context.User;

        if (tenantContext.ContextMode != TenantContextMode.Tenant
            || principal.Identity?.IsAuthenticated != true
            || endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint?.Metadata.GetMetadata<AllowWithoutActiveTrayAttribute>() is not null
            || principal.FindFirstValue("token_type") == TrayDeviceTokenType
            || string.Equals(options.Mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(options.Mode, "Observe", StringComparison.OrdinalIgnoreCase)
                && tenantContext.ContextMode == TenantContextMode.Tenant
                && principal.Identity?.IsAuthenticated == true
                && endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null
                && endpoint?.Metadata.GetMetadata<AllowWithoutActiveTrayAttribute>() is null
                && principal.FindFirstValue("token_type") != TrayDeviceTokenType)
            {
                await LogObserveResultAsync(context, tenantContext, options, logger);
            }

            await _next(context);
            return;
        }

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("user_id");
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            await WriteRequired(context);
            return;
        }

        var repository = context.RequestServices.GetRequiredService<ITrayActivationRepository>();
        var clock = context.RequestServices.GetRequiredService<IDateTimeProvider>();
        var device = await repository.FindLatestActiveDeviceForUserAsync(
            userId, tenantContext.TenantId, context.RequestAborted);
        var connected = device?.LastSeenAt is { } lastSeen
            && lastSeen > clock.UtcNow.AddSeconds(-options.GracePeriodSeconds);

        if (!connected)
        {
            await WriteRequired(context);
            return;
        }

        await _next(context);
    }

    private static async Task LogObserveResultAsync(
        HttpContext context,
        ITenantContext tenantContext,
        TrayPresenceOptions options,
        ILogger<TrayPresenceEnforcementMiddleware> logger)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("user_id");
        if (!Guid.TryParse(userId, out var parsedUserId))
            return;

        var repository = context.RequestServices.GetRequiredService<ITrayActivationRepository>();
        var clock = context.RequestServices.GetRequiredService<IDateTimeProvider>();
        var device = await repository.FindLatestActiveDeviceForUserAsync(
            parsedUserId, tenantContext.TenantId, context.RequestAborted);
        var connected = device?.LastSeenAt is { } lastSeen
            && lastSeen > clock.UtcNow.AddSeconds(-options.GracePeriodSeconds);
        if (!connected)
            logger.LogInformation("tray_presence_would_block");
    }

    private static async Task WriteRequired(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = ActiveTrayRequiredType,
            title = "Active tray connection required",
            status = StatusCodes.Status428PreconditionRequired,
            code = "active_tray_required",
        });
    }
}
