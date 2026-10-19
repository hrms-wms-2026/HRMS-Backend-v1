using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// SEC-01: the tray device-token key is a backend-only HS256 secret. Production must fail at startup
/// when it is missing, too short or a development placeholder, and no message may echo the value.
/// </summary>
public class TrayJwtSecretConfigurationTests
{
    private static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static IConfiguration Config(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigurationStartupValidator.TrayJwtSecretKey] = secret })
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_startup_fails_when_the_secret_is_missing(string? secret)
    {
        var act = () => ConfigurationStartupValidator.ValidateTrayJwtSecret(Config(secret), "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*TrayApp__Jwt__Secret*");
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcde")]   // 31 bytes
    [InlineData("ééééééééééééééé")]                    // 15 chars, 30 UTF-8 bytes
    public void Key_shorter_than_256_bits_is_rejected_for_HS256(string secret)
    {
        var act = () => ConfigurationStartupValidator.ValidateTrayJwtSecret(Config(secret), "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
        ((Action)(() => new TrayTokenService(Config(secret)))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("dev-only-tray-jwt-secret-min-32-chars-replace-in-prod")]
    [InlineData("<local-32-plus-character-random-secret-for-tray-app>")]
    [InlineData("CHANGE_ME_CHANGE_ME_CHANGE_ME_CHANGE_ME")]
    public void Placeholder_is_rejected_in_production_and_the_value_is_never_echoed(string placeholder)
    {
        var act = () => ConfigurationStartupValidator.ValidateTrayJwtSecret(Config(placeholder), "Production");

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().NotContain(placeholder);
    }

    [Fact]
    public void Development_startup_accepts_the_tracked_placeholder_and_a_missing_secret()
    {
        ConfigurationStartupValidator.ValidateTrayJwtSecret(
            Config("dev-only-tray-jwt-secret-min-32-chars-replace-in-prod"), "Development");
        // Development/Test keep the lazy check in the token consumers.
        ConfigurationStartupValidator.ValidateTrayJwtSecret(Config(null), "Development");
    }

    [Fact]
    public void Valid_secret_supplied_through_the_TrayApp__Jwt__Secret_environment_variable_is_accepted()
    {
        const string variable = "TrayApp__Jwt__Secret";
        var previous = Environment.GetEnvironmentVariable(variable);
        var secret = RandomSecret();
        try
        {
            Environment.SetEnvironmentVariable(variable, secret);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            ConfigurationStartupValidator.ValidateTrayJwtSecret(configuration, "Production");
            ConfigurationStartupValidator.RequireTrayJwtSecret(configuration).Should().Be(secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Issued_tray_token_validates_with_the_same_key_and_fails_with_another()
    {
        var secret = RandomSecret();
        var token = new TrayTokenService(Config(secret))
            .GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        TokenValidationParameters Params(string key) => new()
        {
            ValidateIssuer = true,
            ValidIssuer = "onevo-tray",
            ValidateAudience = true,
            ValidAudience = "onevo-tray-app",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true
        };

        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(token, Params(secret), out var validated);
        ((JwtSecurityToken)validated).SignatureAlgorithm.Should().Be(SecurityAlgorithms.HmacSha256);

        var forged = () => handler.ValidateToken(token, Params(RandomSecret()), out _);
        forged.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }
}
