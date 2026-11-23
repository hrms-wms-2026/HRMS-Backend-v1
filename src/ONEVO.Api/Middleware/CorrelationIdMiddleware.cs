namespace ONEVO.Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    public const string CorrelationIdTag = "crownus.correlation_id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId)
            || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items[CorrelationIdHeader] = correlationId.ToString();
        context.Response.Headers[CorrelationIdHeader] = correlationId.ToString();
        // Task 17: on the request span too, so a browser's correlation id finds the request and its SQL calls.
        // The header comes from the client, so the tag is capped.
        var tagValue = correlationId.ToString();
        System.Diagnostics.Activity.Current?.SetTag(CorrelationIdTag, tagValue.Length > 100 ? tagValue[..100] : tagValue);

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId.ToString()))
        {
            await _next(context);
        }
    }
}
