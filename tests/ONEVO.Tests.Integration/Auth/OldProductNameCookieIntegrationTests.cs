using System.Net;
using FluentAssertions;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Hard cutover from the Onevo cookie names to Crownus: a browser still holding the old session
/// cookie is simply signed out once (plain 401), never looped or errored. Kept in its own class
/// so its sign-in does not count against BaseDomainLoginIntegrationTests' per-IP login limit
/// (20 per 5 minutes, process-local, one host per class fixture). Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class OldProductNameCookieIntegrationTests : IClassFixture<BaseDomainLoginIntegrationTestsFixture>
{
    private readonly BaseDomainLoginIntegrationTestsFixture _fixture;

    public OldProductNameCookieIntegrationTests(BaseDomainLoginIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Request_WithOnlyOldOnevoSessionCookie_IsUnauthenticated()
    {
        var user = await _fixture.SeedActiveUserAsync(
            "old-cookie-tenant", "oldcookie@test.crownus.test", "CorrectPass1!");

        var response = await _fixture.PostLoginAsync(user.Email, "CorrectPass1!");
        var legalCompleted = await _fixture.CompleteLegalAcceptanceAsync(response);
        var exchanged = await _fixture.CompleteTenantSessionExchangeAsync(legalCompleted);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());
        var sessionCookie = _fixture.ExtractCookieValue(exchanged, "crownus_session");
        sessionCookie.Should().NotBeNullOrEmpty("sign-in must issue crownus_session");

        using var withNewName = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        withNewName.Headers.Host = "old-cookie-tenant.localhost";
        withNewName.Headers.Add("Cookie", $"crownus_session={sessionCookie}");
        (await _fixture.Client.SendAsync(withNewName)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The same session value under the pre-rename name is just an unknown cookie.
        using var withOldName = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        withOldName.Headers.Host = "old-cookie-tenant.localhost";
        withOldName.Headers.Add("Cookie", $"onevo_session={sessionCookie}");
        (await _fixture.Client.SendAsync(withOldName)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
