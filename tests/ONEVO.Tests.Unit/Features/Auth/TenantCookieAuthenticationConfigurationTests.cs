using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Sessions;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantCookieAuthenticationConfigurationTests
{
    [Fact]
    public void TenantScheme_UsesCookieAuthenticationSlidingExpirationAndTicketStore()
    {
        var options = InvokeAddApiAuthentication();

        Assert.Equal("onevo_session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.True(options.SlidingExpiration);
        Assert.Equal(SessionPolicy.SlidingWindow, options.ExpireTimeSpan);
        Assert.IsType<TenantDatabaseTicketStore>(options.SessionStore);
    }

    [Fact]
    public void TenantScheme_CookieDomain_IsHostScopedOnly()
    {
        // Tenant session exchange (TenantSessionExchangeService) always completes the real sign-in
        // on the tenant host itself, so onevo_session must never carry a shared parent-domain
        // Domain attribute - there is no cross-subdomain cookie sharing to support.
        var options = InvokeAddApiAuthentication();

        Assert.Null(options.Cookie.Domain);
    }

    private static CookieAuthenticationOptions InvokeAddApiAuthentication()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDateTimeProvider>());

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(instance => instance.EnvironmentName).Returns(Environments.Development);

        // AddApiAuthentication now requires IConfiguration for TrayDeviceScheme JWT setup.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrayApp:Jwt:Secret"] = "unit-test-tray-jwt-secret-min-32-chars!!",
                ["TrayApp:Jwt:Issuer"] = "onevo-tray",
                ["TrayApp:Jwt:Audience"] = "onevo-tray-app"
            })
            .Build();

        var extensionsType = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly
            .GetType("ONEVO.Api.Extensions.AuthenticationExtensions", throwOnError: true)!;
        var addAuthentication = extensionsType.GetMethod(
            "AddApiAuthentication",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        addAuthentication.Invoke(null, [services, environment.Object, configuration]);

        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get("TenantScheme");
    }
}
