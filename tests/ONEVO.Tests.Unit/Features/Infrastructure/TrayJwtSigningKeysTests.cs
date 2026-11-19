using System.Security.Cryptography;
using FluentAssertions;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;
using ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// SEC-15 (audit F-17): tray device tokens are signed with ES256 keys that carry a kid. Outside
/// Development/Test the API must refuse to start with a missing, broken or ambiguous key set, and no
/// message may echo key material.
/// </summary>
public class TrayJwtSigningKeysTests
{
    private static TrayJwtKeySet Load(Dictionary<string, string?> settings, string environment = "Production") =>
        TrayJwtKeyLoader.Load(TrayJwtTestKeys.Config(settings), environment);

    [Fact]
    public void Valid_key_set_loads_with_the_active_kid()
    {
        var settings = TrayJwtTestKeys.Settings(
            [("tray-2026-11", TrayJwtTestKeys.NewPem()), ("tray-2026-12", TrayJwtTestKeys.NewPem())],
            activeKid: "tray-2026-12");

        var keys = Load(settings);

        keys.ActiveKey.KeyId.Should().Be("tray-2026-12");
        keys.ValidationKeys.Keys.Should().BeEquivalentTo("tray-2026-11", "tray-2026-12");
        keys.LegacyKey.Should().BeNull();
    }

    [Fact]
    public void Missing_key_set_fails_outside_development()
    {
        var act = () => Load(new Dictionary<string, string?>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*TrayApp__Jwt__SigningKeys*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-listed")]
    public void Active_kid_must_name_a_listed_key(string? activeKid)
    {
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:ActiveKid"] = activeKid;

        var act = () => Load(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ActiveKid*");
    }

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\nbm90LWEta2V5\n-----END PRIVATE KEY-----")]
    [InlineData("<ecdsa-p256-private-key-pem>")]
    [InlineData("SET_VIA_ENV")]
    [InlineData("")]
    public void Broken_or_placeholder_pem_fails_and_is_never_echoed(string pem)
    {
        var settings = TrayJwtTestKeys.Settings([("tray-2026-11", pem)]);

        var act = () => Load(settings);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("tray-2026-11");
        if (pem.Length > 0)
            ex.Message.Should().NotContain(pem);
        ex.InnerException.Should().BeNull("the parser's message could carry key material");
    }

    [Fact]
    public void Non_P256_key_is_rejected_because_the_algorithm_is_pinned_to_ES256()
    {
        var settings = TrayJwtTestKeys.Settings([("p384", TrayJwtTestKeys.NewPem(ECCurve.NamedCurves.nistP384))]);

        var act = () => Load(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*P-256*");
    }

    [Fact]
    public void Duplicate_kid_is_rejected()
    {
        var settings = TrayJwtTestKeys.Settings([("same", TrayJwtTestKeys.NewPem()), ("same", TrayJwtTestKeys.NewPem())]);

        var act = () => Load(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*same*more than once*");
    }

    [Fact]
    public void Blank_kid_is_rejected()
    {
        var settings = TrayJwtTestKeys.Settings([(" ", TrayJwtTestKeys.NewPem())], activeKid: " ");

        var act = () => Load(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Kid*");
    }

    [Fact]
    public void Pem_from_an_environment_variable_with_escaped_newlines_loads()
    {
        var escaped = TrayJwtTestKeys.NewPem().Replace("\r\n", "\n").Replace("\n", "\\n");
        var settings = TrayJwtTestKeys.Settings([("env", escaped)]);

        Load(settings).ActiveKey.KeyId.Should().Be("env");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void Local_environments_get_a_throwaway_key_when_none_is_configured(string environment)
    {
        var keys = Load(new Dictionary<string, string?>(), environment);

        keys.ActiveKey.KeyId.Should().StartWith("dev-ephemeral-");
        keys.IsEphemeral.Should().BeTrue();
    }

    [Fact]
    public void Configured_keys_are_still_checked_in_development()
    {
        var settings = TrayJwtTestKeys.Settings([("dev", "<placeholder>")]);

        var act = () => Load(settings, "Development");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Legacy_window_loads_the_old_secret_and_its_end_time()
    {
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = TrayJwtTestKeys.NewLegacySecret();
        settings["TrayApp:Jwt:AcceptLegacyHs256Until"] = "2026-11-20T12:00:00Z";

        var keys = Load(settings);

        keys.LegacyKey.Should().NotBeNull();
        keys.LegacyUntil.Should().Be(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Old_secret_is_ignored_when_no_legacy_window_is_set()
    {
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = TrayJwtTestKeys.NewLegacySecret();

        Load(settings).LegacyKey.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    [InlineData("dev-only-tray-jwt-secret-min-32-chars-replace-in-prod")]
    public void Legacy_window_needs_a_real_old_secret_outside_development(string? secret)
    {
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = secret;
        settings["TrayApp:Jwt:AcceptLegacyHs256Until"] = "2026-11-20T12:00:00Z";

        var act = () => Load(settings);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("TrayApp:Jwt:Secret");
        if (secret is not null)
            ex.Message.Should().NotContain(secret);
    }

    [Theory]
    [InlineData("next tuesday")]
    [InlineData("2026-11-20")]   // no time and no offset: ambiguous
    public void Legacy_window_end_must_be_an_explicit_utc_instant(string value)
    {
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = TrayJwtTestKeys.NewLegacySecret();
        settings["TrayApp:Jwt:AcceptLegacyHs256Until"] = value;

        var act = () => Load(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AcceptLegacyHs256Until*");
    }

    [Fact]
    public void Startup_check_fails_in_production_and_is_skipped_locally()
    {
        var empty = TrayJwtTestKeys.Config(new Dictionary<string, string?>());

        var production = () => ConfigurationStartupValidator.ValidateTrayJwtSigningKeys(empty, "Production");
        production.Should().Throw<InvalidOperationException>();

        ConfigurationStartupValidator.ValidateTrayJwtSigningKeys(empty, "Development");
        ConfigurationStartupValidator.ValidateTrayJwtSigningKeys(empty, "Test");
    }

    [Fact]
    public void Startup_check_accepts_a_valid_key_set_in_production()
    {
        var config = TrayJwtTestKeys.Config(TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]));

        ConfigurationStartupValidator.ValidateTrayJwtSigningKeys(config, "Production");
    }

    [Fact]
    public void Legacy_secret_rules_still_apply_to_the_old_secret()
    {
        var shortSecret = TrayJwtTestKeys.Config(new Dictionary<string, string?>
        {
            [ConfigurationStartupValidator.TrayJwtSecretKey] = "0123456789abcdef0123456789abcde" // 31 bytes
        });

        var act = () => ConfigurationStartupValidator.RequireTrayJwtSecret(shortSecret);

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }
}
