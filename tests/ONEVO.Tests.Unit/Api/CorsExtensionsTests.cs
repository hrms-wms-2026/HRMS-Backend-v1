using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ONEVO.Api.Extensions;

namespace ONEVO.Tests.Unit.Api;

public sealed class CorsExtensionsTests
{
    private static CorsPolicy BuildPolicy(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddApiCors(configuration);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy("AllowFrontend")!;
    }

    private static readonly Dictionary<string, string?> TenantSettings = new()
    {
        ["Tenancy:RootDomain"] = "onevo.com",
        ["AllowedOrigins:0"] = "http://localhost:5173",
    };

    [Theory]
    [InlineData("https://onevo.com")]
    [InlineData("https://acme.onevo.com")]
    [InlineData("https://acme.onevo.com:4200")]
    public void HttpsTenantOrigins_AreAllowed(string origin)
    {
        BuildPolicy(TenantSettings).IsOriginAllowed(origin).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://onevo.com")]
    [InlineData("http://acme.onevo.com")]
    [InlineData("ws://acme.onevo.com")]
    public void NonHttpsTenantOrigins_AreRejected(string origin)
    {
        BuildPolicy(TenantSettings).IsOriginAllowed(origin).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://evilonevo.com")]
    [InlineData("https://onevo.com.evil.com")]
    [InlineData("https://evil.com")]
    public void LookalikeOrigins_AreRejected(string origin)
    {
        BuildPolicy(TenantSettings).IsOriginAllowed(origin).Should().BeFalse();
    }

    [Fact]
    public void ExplicitlyConfiguredOrigin_IsAllowedEvenOverHttp()
    {
        BuildPolicy(TenantSettings).IsOriginAllowed("http://localhost:5173").Should().BeTrue();
    }

    [Fact]
    public void NoRootDomain_OnlyExplicitOriginsAreAllowed()
    {
        var policy = BuildPolicy(new() { ["AllowedOrigins:0"] = "https://app.onevo.com" });

        policy.IsOriginAllowed("https://app.onevo.com").Should().BeTrue();
        policy.IsOriginAllowed("https://acme.onevo.com").Should().BeFalse();
    }
}
