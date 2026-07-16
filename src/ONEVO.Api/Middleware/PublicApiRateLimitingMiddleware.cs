using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ONEVO.Api.Middleware;

public sealed class PublicApiRateLimitingMiddleware
{
    private static readonly PublicApiRateLimitRule[] Rules =
    [
        new("/api/v1", 300, TimeSpan.FromMinutes(1)),
        new("/admin/v1", 120, TimeSpan.FromMinutes(1))
    ];

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PublicApiRateLimitingMiddleware> _logger;

    public PublicApiRateLimitingMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<PublicApiRateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var rule = Rules.FirstOrDefault(r => path.StartsWith(r.PathPrefix, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            await _next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = BuildCacheKey(rule, ip);
        var bucket = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = rule.Window;
            return new RateLimitBucket();
        })!;

        int count;
        lock (bucket)
        {
            bucket.Count++;
            count = bucket.Count;
        }

        if (count <= rule.MaxRequests)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Public API rate limit exceeded for {PathPrefix}. Limit={Limit}, WindowSeconds={WindowSeconds}",
            rule.PathPrefix,
            rule.MaxRequests,
            (int)rule.Window.TotalSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = ((int)rule.Window.TotalSeconds).ToString();
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://onevo.com/errors/rate-limit",
            title = "Too Many Requests",
            status = 429,
            detail = "Too many requests. Please try again later."
        });
    }

    private static string BuildCacheKey(PublicApiRateLimitRule rule, string ip)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
        return $"rate-limit:public:{rule.PathPrefix}:{hash}";
    }

    private sealed class RateLimitBucket
    {
        public int Count { get; set; }
    }

    private sealed record PublicApiRateLimitRule(
        string PathPrefix,
        int MaxRequests,
        TimeSpan Window);
}
