using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;

/// <summary>Shared bits of the three rate limit controllers (Improvements task 47).</summary>
internal static class RateLimitControllerSupport
{
    private const string CorrelationIdItem = "X-Correlation-Id";

    /// <summary>
    /// The 400 for a limit name outside the closed list of six. Login, MFA and password reset limits
    /// and the master switch are not in it, so no route can change them.
    /// </summary>
    public static IActionResult UnknownLimit(ControllerBase controller, string limitName) =>
        controller.Problem(
            $"Unknown rate limit '{limitName}'. Allowed: {string.Join(", ", RateLimitNames.All)}.",
            statusCode: StatusCodes.Status400BadRequest);

    public static string? CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdItem, out var value) && value is string id ? id : context.TraceIdentifier;

    public static (string Reason, string? RowVersion) RemoveArguments(
        RemoveRateLimitRequest? body, string? reason, string? rowVersion) =>
        (body?.Reason ?? reason ?? string.Empty, body?.RowVersion ?? rowVersion);
}
