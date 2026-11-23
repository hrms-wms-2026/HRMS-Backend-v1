using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ONEVO.Api.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Auth;

/// <summary>
/// Task 15: the fallback policy's scheme must pick the scheme that matches the caller, so a tray
/// device or admin is never refused just because the fallback only looked at the tenant cookie.
/// </summary>
public sealed class FallbackAuthenticationSchemeTests
{
    private static DefaultHttpContext Request(string path, string? authorization = null, string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Theory]
    [InlineData("/api/v1/employees")]
    [InlineData("/hubs/inbox")]
    [InlineData("/something/unmapped")]
    public void TenantPaths_WithoutBearer_UseTenantScheme(string path) =>
        FallbackAuthenticationScheme.Select(Request(path)).Should().Be("TenantScheme");

    [Theory]
    [InlineData("/admin/v1/tenants")]
    [InlineData("/ADMIN/v1/auth/me")]
    public void AdminPaths_UseAdminScheme(string path) =>
        FallbackAuthenticationScheme.Select(Request(path)).Should().Be("AdminScheme");

    [Fact]
    public void AdminLookalikePath_IsNotAdmin() =>
        FallbackAuthenticationScheme.Select(Request("/administrator")).Should().Be("TenantScheme");

    [Theory]
    [InlineData("/api/v1/monitoring/activation/heartbeat")]
    [InlineData("/admin/v1/anything")]
    public void BearerHeader_UsesTrayDeviceScheme(string path) =>
        FallbackAuthenticationScheme.Select(Request(path, authorization: "Bearer abc.def.ghi"))
            .Should().Be("TrayDeviceScheme");

    [Fact]
    public void AgentHub_WithAccessTokenQuery_UsesTrayDeviceScheme() =>
        FallbackAuthenticationScheme.Select(Request("/hubs/agent-commands/negotiate", query: "?access_token=abc"))
            .Should().Be("TrayDeviceScheme");

    [Fact]
    public void AccessTokenQuery_OutsideAgentHub_IsIgnored() =>
        FallbackAuthenticationScheme.Select(Request("/api/v1/employees", query: "?access_token=abc"))
            .Should().Be("TenantScheme");
}
