using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

/// <summary>
/// Client details of the current HTTP request. The IP is the connection's remote address after
/// UseForwardedHeaders, the same source the admin login events use. The correlation id is the one
/// CorrelationIdMiddleware stored for the request. Outside a request every value is null.
/// </summary>
public sealed class HttpRequestClientInfo : IRequestClientInfo
{
    private const string CorrelationIdItemKey = "X-Correlation-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestClientInfo(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string? CorrelationId
        => _httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdItemKey, out var value) == true
            ? value as string
            : null;
}
