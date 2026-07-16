using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Filters;

/// <summary>
/// Honors the Idempotency-Key request header on critical mutating endpoints.
/// Same key + same body → the stored original response is replayed.
/// Same key + different body → 409 Conflict.
/// Same key while the original request is still running → 409 Conflict.
/// Requests without the header pass through unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            await next();
            return;
        }

        if (key.Length > 200)
        {
            context.Result = Problem(context, 400, "Idempotency-Key must be at most 200 characters.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IIdempotencyStore>();
        var scope = $"{context.HttpContext.Request.Method}:{context.HttpContext.Request.Path}";
        var requesterId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var requestHash = HashArguments(context.ActionArguments);

        var begin = await store.TryBeginAsync(
            key, scope, requesterId, requestHash, Ttl, context.HttpContext.RequestAborted);

        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Replay:
                context.HttpContext.Response.Headers["Idempotency-Replayed"] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = begin.ResponseStatusCode,
                    Content = begin.ResponseBody,
                    ContentType = "application/json"
                };
                return;

            case IdempotencyOutcome.HashMismatch:
                context.Result = Problem(context, 409,
                    "This Idempotency-Key was already used with a different request body.");
                return;

            case IdempotencyOutcome.InFlight:
                context.Result = Problem(context, 409,
                    "A request with this Idempotency-Key is still being processed.");
                return;
        }

        var executed = await next();

        var ct = context.HttpContext.RequestAborted;
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            // Unhandled failure → 500; drop the marker so the client can retry.
            await store.AbandonAsync(begin.RecordId, CancellationToken.None);
            return;
        }

        var (statusCode, body) = ExtractResponse(executed.Result);
        if (statusCode >= 500)
        {
            await store.AbandonAsync(begin.RecordId, ct);
            return;
        }

        await store.CompleteAsync(begin.RecordId, statusCode, body, ct);
    }

    private static (int StatusCode, string? Body) ExtractResponse(IActionResult? result) => result switch
    {
        ObjectResult obj => (
            obj.StatusCode ?? 200,
            obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, ResponseJson)),
        StatusCodeResult status => (status.StatusCode, null),
        _ => (200, null)
    };

    private static string HashArguments(IDictionary<string, object?> arguments)
    {
        // Hash the model-bound arguments (route values + body DTO). CancellationToken
        // is excluded since it is not part of the request identity.
        var relevant = arguments
            .Where(kv => kv.Value is not CancellationToken)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var json = JsonSerializer.Serialize(relevant);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ObjectResult Problem(ActionExecutingContext context, int status, string detail) =>
        new(new
        {
            type = "https://onevo.com/errors/idempotency",
            title = status == 409 ? "Conflict" : "Bad Request",
            status,
            detail,
            correlationId = context.HttpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
        })
        { StatusCode = status };
}
