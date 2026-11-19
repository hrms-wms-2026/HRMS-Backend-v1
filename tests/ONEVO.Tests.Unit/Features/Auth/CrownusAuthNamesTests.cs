using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ONEVO.Api.Auth;
using ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// The product is Crownus: cookies and token labels carry the Crownus name, and anything a
/// browser or tray still holds under the old Onevo names is simply not accepted (hard cutover).
/// </summary>
public sealed class CrownusAuthNamesTests
{
    private const string TrayKid = "unit-test";
    private static readonly string TrayKeyPem = TrayJwtTestKeys.NewPem();

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
        // Signed with a listed key, so only the old issuer and audience can make it fail.
        var key = new ECDsaSecurityKey(LoadKey(TrayKeyPem)) { KeyId = TrayKid };
        var oldToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "onevo-tray",
            Audience = "onevo-tray-app",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(oldToken, parameters);

        result.IsValid.Should().BeFalse("a tray holding a pre-rename token must re-pair");
    }

    private static ECDsa LoadKey(string pem)
    {
        var ec = ECDsa.Create();
        ec.ImportFromPem(pem);
        return ec;
    }

    private static TokenValidationParameters TrayValidationParameters() =>
        TrayJwtTestKeys.BuildServices(
                TrayJwtTestKeys.Config(TrayJwtTestKeys.Settings([(TrayKid, TrayKeyPem)])))
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("TrayDeviceScheme")
            .TokenValidationParameters;
}
