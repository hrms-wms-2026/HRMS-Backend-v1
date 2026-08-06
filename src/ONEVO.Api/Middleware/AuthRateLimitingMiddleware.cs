using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Phase 1 process-local login limiter. This is valid only while deployment is restricted to one
/// API instance; it must be replaced by an approved distributed limiter before horizontal scale.
/// </summary>
public sealed class AuthRateLimitingMiddleware
{
    private const string MfaCookieName = "onevo_mfa";
    private const string AdminMfaCookieName = "admin_mfa";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly AuthRateLimitRule[] Rules =
    [
        new("/api/v1/auth/login", "ip", null, 20, TimeSpan.FromSeconds(300)),
        new("/api/v1/auth/login", "email", "email", 5, TimeSpan.FromSeconds(300)),

        new("/api/v1/auth/login/select-workspace", "ip", null, 20, TimeSpan.FromSeconds(300)),
        new("/api/v1/auth/login/select-workspace", "challenge", "login_challenge", 10, TimeSpan.FromMinutes(5)),

        new("/api/v1/auth/login/google", "ip", null, 20, TimeSpan.FromSeconds(300)),
        new("/api/v1/auth/login/google", "token", "google_id_token", 5, TimeSpan.FromSeconds(300)),

        new("/api/v1/auth/mfa/verify", "ip", null, 20, TimeSpan.FromMinutes(5)),
        new("/api/v1/auth/mfa/verify", "mfa", null, 5, TimeSpan.FromMinutes(10)),

        new("/api/v1/auth/forgot-password", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/api/v1/auth/forgot-password", "email", "email", 3, TimeSpan.FromMinutes(15)),

        new("/api/v1/auth/reset-password", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/api/v1/auth/reset-password", "token", "token", 5, TimeSpan.FromMinutes(15)),

        new("/api/v1/auth/force-change-password", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/api/v1/auth/force-change-password", "email", "email", 5, TimeSpan.FromMinutes(15)),

        new("/admin/v1/auth/login", "ip", null, 20, TimeSpan.FromSeconds(300)),
        new("/admin/v1/auth/login", "email", "email", 5, TimeSpan.FromSeconds(300)),

        new("/admin/v1/auth/mfa/verify", "ip", null, 20, TimeSpan.FromMinutes(5)),
        new("/admin/v1/auth/mfa/verify", "mfa", null, 5, TimeSpan.FromMinutes(10), CookieName: AdminMfaCookieName),

        new("/admin/v1/auth/forgot-password", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/admin/v1/auth/forgot-password", "email", "email", 3, TimeSpan.FromMinutes(15)),

        new("/admin/v1/auth/reset-password", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/admin/v1/auth/reset-password", "token", "token", 5, TimeSpan.FromMinutes(15)),

        new("/api/v1/auth/invitations", "ip", null, 20, TimeSpan.FromMinutes(15), PrefixMatch: true),
        new("/api/v1/auth/invitations", "path", null, 5, TimeSpan.FromMinutes(15), PrefixMatch: true)
    ];

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthRateLimitingMiddleware> _logger;

    public AuthRateLimitingMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<AuthRateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var matchedRules = Rules.Where(rule => rule.Matches(path)).ToArray();
        if (matchedRules.Length == 0)
        {
            await _next(context);
            return;
        }

        var bodyFields = await ReadBodyFieldsAsync(context, matchedRules);
        foreach (var rule in matchedRules)
        {
            var identity = ResolveIdentity(context, path, bodyFields, rule);
            if (string.IsNullOrWhiteSpace(identity))
                continue;

            var key = BuildCacheKey(rule, identity);
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
                continue;

            _logger.LogWarning(
                "Rate limit exceeded for {Path}. Scope={Scope}, Limit={Limit}, WindowSeconds={WindowSeconds}",
                path,
                rule.Scope,
                rule.MaxRequests,
                (int)rule.Window.TotalSeconds);

            await WriteRateLimitExceededResponseAsync(context, rule);
            return;
        }

        await _next(context);
    }

    private static async Task WriteRateLimitExceededResponseAsync(HttpContext context, AuthRateLimitRule rule)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = ((int)rule.Window.TotalSeconds).ToString();
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://onevo.com/errors/rate-limit",
            title = "Too Many Requests",
            status = 429,
            detail = "Too many attempts. Please try again later."
        });
    }

    private static async Task<Dictionary<string, string>> ReadBodyFieldsAsync(
        HttpContext context,
        IReadOnlyList<AuthRateLimitRule> rules)
    {
        if (!rules.Any(rule => rule.BodyField is not null))
            return [];

        if (context.Request.ContentLength is null or 0)
            return [];

        context.Request.EnableBuffering();

        try
        {
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
                return [];

            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body, JsonOptions);
            if (values is null)
                return [];

            return values
                .Where(pair => pair.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.GetString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
            return [];
        }
    }

    private static string ResolveIdentity(
        HttpContext context,
        string path,
        IReadOnlyDictionary<string, string> bodyFields,
        AuthRateLimitRule rule)
    {
        if (rule.Scope == "ip")
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (rule.Scope == "path")
            return path;

        if (rule.Scope == "mfa")
        {
            var cookieName = rule.CookieName ?? MfaCookieName;
            if (context.Request.Cookies.TryGetValue(cookieName, out var mfaCookie)
                && !string.IsNullOrWhiteSpace(mfaCookie))
            {
                return mfaCookie;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        if (rule.BodyField is null)
            return string.Empty;

        return bodyFields.TryGetValue(rule.BodyField, out var value)
            ? value.Trim().ToLowerInvariant()
            : string.Empty;
    }

    private static string BuildCacheKey(AuthRateLimitRule rule, string identity)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"rate-limit:auth:{rule.Path}:{rule.Scope}:{hash}";
    }

    private sealed class RateLimitBucket
    {
        public int Count { get; set; }
    }

    private sealed record AuthRateLimitRule(
        string Path,
        string Scope,
        string? BodyField,
        int MaxRequests,
        TimeSpan Window,
        bool PrefixMatch = false,
        string? CookieName = null)
    {
        public bool Matches(string path) =>
            PrefixMatch
                ? path.StartsWith(Path, StringComparison.OrdinalIgnoreCase)
                : string.Equals(path, Path, StringComparison.OrdinalIgnoreCase);
    }
}
