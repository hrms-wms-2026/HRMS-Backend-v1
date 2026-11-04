using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.RequestContext;

/// <summary>
/// Reads audit metadata from the current HTTP request. The IP is the connection's remote address
/// after UseForwardedHeaders has applied the trusted-proxy rules. Values are truncated to the
/// audit column lengths because the User-Agent and correlation id are client-supplied.
/// </summary>
public sealed class HttpRequestAuditContext : IRequestAuditContext
{
    // Kept in step with CorrelationIdMiddleware, which stores the id under this key.
    private const string CorrelationIdItemKey = "X-Correlation-Id";
    private const int MaxIpLength = 45;
    private const int MaxUserAgentLength = 512;
    private const int MaxCorrelationIdLength = 64;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestAuditContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? IpAddress =>
        Truncate(_httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), MaxIpLength);

    public string? UserAgent =>
        Truncate(_httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(), MaxUserAgentLength);

    public string? CorrelationId =>
        Truncate(_httpContextAccessor.HttpContext?.Items[CorrelationIdItemKey]?.ToString(), MaxCorrelationIdLength);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
