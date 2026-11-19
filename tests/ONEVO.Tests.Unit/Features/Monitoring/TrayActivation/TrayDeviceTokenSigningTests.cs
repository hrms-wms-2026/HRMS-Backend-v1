using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

/// <summary>
/// SEC-15 (audit F-17): the API signs tray device tokens with an ES256 key named by its kid, and
/// "TrayDeviceScheme" accepts only ES256 tokens whose kid names a listed key, plus (during an
/// optional cut-over window) the old HS256 tokens. The claims and lifetime the tray app reads must
/// not change.
/// </summary>
public class TrayDeviceTokenSigningTests
{
    private const string Issuer = "crownus-tray";
    private const string Audience = "crownus-tray-app";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed record Rig(ServiceProvider Services, ManualTimeProvider Time)
    {
        public ITrayTokenService Tokens => Services.GetRequiredService<ITrayTokenService>();

        public TokenValidationParameters Parameters => Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("TrayDeviceScheme")
            .TokenValidationParameters;

        public async Task<bool> IsValid(string token) =>
            (await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters)).IsValid;
    }

    private static Rig Build(Dictionary<string, string?> settings, string environment = "Production")
    {
        var time = new ManualTimeProvider(Now);
        return new Rig(TrayJwtTestKeys.BuildServices(TrayJwtTestKeys.Config(settings), environment, time), time);
    }

    private static string Sign(SecurityKey key, string algorithm, string? kidOverride = null, bool dropKid = false)
    {
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("token_type", "tray_device")],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, algorithm));
        if (kidOverride is not null) token.Header["kid"] = kidOverride;
        if (dropKid) token.Header.Remove("kid");
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ECDsaSecurityKey KeyFrom(string pem, string kid)
    {
        var ec = ECDsa.Create();
        ec.ImportFromPem(pem);
        return new ECDsaSecurityKey(ec) { KeyId = kid };
    }

    private static JsonElement Part(string token, int index) =>
        JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[index])).RootElement;

    [Fact]
    public async Task Issued_token_is_ES256_with_the_active_kid_and_keeps_the_tray_claims_and_lifetime()
    {
        var rig = Build(TrayJwtTestKeys.Settings([("tray-2026-11", TrayJwtTestKeys.NewPem())]));
        var device = Guid.NewGuid(); var user = Guid.NewGuid(); var tenant = Guid.NewGuid(); var entity = Guid.NewGuid();

        var token = rig.Tokens.GenerateAccessToken(device, user, tenant, entity);

        var header = Part(token, 0);
        header.GetProperty("alg").GetString().Should().Be("ES256");
        header.GetProperty("kid").GetString().Should().Be("tray-2026-11");

        var payload = Part(token, 1);
        payload.GetProperty("sub").GetString().Should().Be(device.ToString());
        payload.GetProperty("user_id").GetString().Should().Be(user.ToString());
        payload.GetProperty("tenant_id").GetString().Should().Be(tenant.ToString());
        payload.GetProperty("legal_entity_id").GetString().Should().Be(entity.ToString());
        payload.GetProperty("token_type").GetString().Should().Be("tray_device");
        payload.GetProperty("iss").GetString().Should().Be(Issuer);
        payload.GetProperty("aud").GetString().Should().Be(Audience);
        var expiry = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());
        expiry.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));

        (await rig.IsValid(token)).Should().BeTrue();
    }

    [Fact]
    public void Legal_entity_claim_is_still_omitted_when_there_is_none()
    {
        var rig = Build(TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]));

        var token = rig.Tokens.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        Part(token, 1).TryGetProperty("legal_entity_id", out _).Should().BeFalse();
    }

    [Fact]
    public void Scheme_pins_ES256_requires_a_matching_kid_and_keeps_issuer_audience_and_skew()
    {
        var p = Build(TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())])).Parameters;

        p.ValidAlgorithms.Should().BeEquivalentTo(SecurityAlgorithms.EcdsaSha256);
        p.TryAllIssuerSigningKeys.Should().BeFalse();
        p.ValidIssuer.Should().Be(Issuer);
        p.ValidAudience.Should().Be(Audience);
        p.ValidateLifetime.Should().BeTrue();
        p.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Token_signed_by_a_second_listed_key_still_validates_during_rotation()
    {
        var oldPem = TrayJwtTestKeys.NewPem();
        var rig = Build(TrayJwtTestKeys.Settings(
            [("old", oldPem), ("new", TrayJwtTestKeys.NewPem())], activeKid: "new"));

        (await rig.IsValid(Sign(KeyFrom(oldPem, "old"), SecurityAlgorithms.EcdsaSha256))).Should().BeTrue();
    }

    [Fact]
    public async Task Token_signed_by_a_key_that_is_not_listed_is_rejected()
    {
        var rig = Build(TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]));

        (await rig.IsValid(Sign(KeyFrom(TrayJwtTestKeys.NewPem(), "a"), SecurityAlgorithms.EcdsaSha256)))
            .Should().BeFalse("the kid matches but the signature is from another key");
        (await rig.IsValid(Sign(KeyFrom(TrayJwtTestKeys.NewPem(), "x"), SecurityAlgorithms.EcdsaSha256)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Token_signed_by_a_listed_key_but_carrying_an_unlisted_kid_is_rejected()
    {
        var pem = TrayJwtTestKeys.NewPem();
        var rig = Build(TrayJwtTestKeys.Settings([("a", pem)]));

        (await rig.IsValid(Sign(KeyFrom(pem, "a"), SecurityAlgorithms.EcdsaSha256, kidOverride: "bogus")))
            .Should().BeFalse();
        (await rig.IsValid(Sign(KeyFrom(pem, "a"), SecurityAlgorithms.EcdsaSha256, dropKid: true)))
            .Should().BeFalse("an ES256 token must name its key");
    }

    [Fact]
    public async Task Old_HS256_token_is_rejected_when_no_legacy_window_is_set()
    {
        var secret = TrayJwtTestKeys.NewLegacySecret();
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = secret;
        var rig = Build(settings);

        var legacy = Sign(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);

        (await rig.IsValid(legacy)).Should().BeFalse();
    }

    [Fact]
    public async Task Old_HS256_token_is_accepted_inside_the_window_and_rejected_after_it_without_a_restart()
    {
        var secret = TrayJwtTestKeys.NewLegacySecret();
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = secret;
        settings["TrayApp:Jwt:AcceptLegacyHs256Until"] = Now.AddHours(1).ToString("O");
        var rig = Build(settings);
        var legacy = Sign(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);

        (await rig.IsValid(legacy)).Should().BeTrue("inside the window");
        (await rig.IsValid(rig.Tokens.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null)))
            .Should().BeTrue("new ES256 tokens work during the window");

        rig.Time.Now = Now.AddHours(1).AddSeconds(1);

        (await rig.IsValid(legacy)).Should().BeFalse("the window has closed");
    }

    [Fact]
    public async Task HS256_token_that_names_an_ES256_kid_is_rejected_even_inside_the_window()
    {
        var secret = TrayJwtTestKeys.NewLegacySecret();
        var settings = TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]);
        settings["TrayApp:Jwt:Secret"] = secret;
        settings["TrayApp:Jwt:AcceptLegacyHs256Until"] = Now.AddHours(1).ToString("O");
        var rig = Build(settings);

        var confused = Sign(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256,
            kidOverride: "a");

        (await rig.IsValid(confused)).Should().BeFalse();
    }

    [Fact]
    public async Task Alg_none_and_a_tampered_payload_are_rejected()
    {
        var rig = Build(TrayJwtTestKeys.Settings([("a", TrayJwtTestKeys.NewPem())]));
        var token = rig.Tokens.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var parts = token.Split('.');

        var unsigned = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"kid\":\"a\",\"typ\":\"JWT\"}") + "." + parts[1] + ".";
        (await rig.IsValid(unsigned)).Should().BeFalse();

        var otherTenant = Base64UrlEncoder.Decode(parts[1]).Replace("tray_device", "tray_devicX");
        (await rig.IsValid(parts[0] + "." + Base64UrlEncoder.Encode(otherTenant) + "." + parts[2])).Should().BeFalse();
    }

    [Fact]
    public async Task Development_issuer_and_validator_share_one_throwaway_key()
    {
        var rig = Build(new Dictionary<string, string?>(), environment: "Development");

        var token = rig.Tokens.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        Part(token, 0).GetProperty("kid").GetString().Should().StartWith("dev-ephemeral-");
        (await rig.IsValid(token)).Should().BeTrue();
    }
}
