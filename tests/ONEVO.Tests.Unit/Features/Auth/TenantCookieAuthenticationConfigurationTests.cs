using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantCookieAuthenticationConfigurationTests
{
    [Fact]
    public void TenantScheme_UsesCookieAuthenticationSlidingExpirationAndTicketStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDateTimeProvider>());

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(instance => instance.EnvironmentName).Returns(Environments.Development);

        var extensionsType = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthController).Assembly
            .GetType("ONEVO.Api.Extensions.AuthenticationExtensions", throwOnError: true)!;
        var addAuthentication = extensionsType.GetMethod(
            "AddApiAuthentication",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        addAuthentication.Invoke(null, [services, environment.Object]);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get("TenantScheme");

        Assert.Equal("onevo_session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.True(options.SlidingExpiration);
        Assert.Equal(SessionPolicy.SlidingWindow, options.ExpireTimeSpan);
        Assert.IsType<TenantDatabaseTicketStore>(options.SessionStore);
    }
}
