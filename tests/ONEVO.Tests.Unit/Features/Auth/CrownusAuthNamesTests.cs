using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using ONEVO.Api.Auth;
using ONEVO.Infrastructure.Identity.Tokens;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// The product is Crownus: cookies and token labels carry the Crownus name, and anything a
/// browser or tray still holds under the old Onevo names is simply not accepted (hard cutover).
/// </summary>
public sealed class CrownusAuthNamesTests
{
    private const string TraySecret = "unit-test-tray-secret-that-is-long-enough-0123456789";

    [Fact]
    public void AuthCookieNames_AreCrownus()
    {
        AuthCookieNames.Session.Should().Be("crownus_session");
        AuthCookieNames.Csrf.Should().Be("crownus_csrf");
        AuthCookieNames.Mfa.Should().Be("crownus_mfa");
        AuthCookieNames.LegalPending.Should().Be("crownus_legal_pending");
        AuthCookieNames.LegalCsrf.Should().Be("crownus_legal_csrf");
        AuthCookieNames.LastTenant.Should().Be("crownus_last_tenant");
    }

    [Fact]
    public void TrayBearer_DefaultsToCrownusIssuerAndAudience()
    {
        var parameters = TrayValidationParameters();

        parameters.ValidIssuer.Should().Be("crownus-tray");
        parameters.ValidAudience.Should().Be("crownus-tray-app");
    }

    [Fact]
    public async Task TrayBearer_RejectsTokenWithOldOnevoIssuerAndAudience()
    {
        var parameters = TrayValidationParameters();
        var oldToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "onevo-tray",
            Audience = "onevo-tray-app",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TraySecret)), SecurityAlgorithms.HmacSha256)
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(oldToken, parameters);

        result.IsValid.Should().BeFalse("a tray holding a pre-rename token must re-pair");
    }

    [Fact]
    public void DeviceToken_DefaultsToCrownusIssuerAndAgentAudience()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = TraySecret })
            .Build();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            new JwtTokenService(configuration).GenerateDeviceToken(Guid.NewGuid(), Guid.NewGuid()));

        token.Issuer.Should().Be("crownus-api");
        token.Audiences.Should().ContainSingle().Which.Should().Be("crownus-agent");
    }

    private static TokenValidationParameters TrayValidationParameters() =>
        BuildAuthServices()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("TrayDeviceScheme")
            .TokenValidationParameters;

    private static ServiceProvider BuildAuthServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TrayApp:Jwt:Secret"] = TraySecret })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Production");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        var extensions = typeof(AuthCookieNames).Assembly
            .GetType("ONEVO.Api.Extensions.AuthenticationExtensions", throwOnError: true)!;
        extensions
            .GetMethod("AddApiAuthentication", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, [services, environment.Object, configuration]);

        return services.BuildServiceProvider();
    }
}
