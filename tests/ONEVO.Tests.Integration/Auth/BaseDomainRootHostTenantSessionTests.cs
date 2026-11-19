using System.Net;
using System.Text.Json;
using FluentAssertions;
using ONEVO.Api.Middleware;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Subdomain mode: the bare root host resolves to System context. A tenant session sent there must
/// be refused by TenantEnforcementMiddleware, because the TenantOrSystem / PlatformShared RLS
/// predicates match system mode across tenants. Uses its own fixture instance (own app and auth
/// rate-limit state), so its login does not count against BaseDomainLoginIntegrationTests' budget.
/// Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class BaseDomainRootHostTenantSessionTests : IClassFixture<BaseDomainLoginIntegrationTestsFixture>
{
    private const string Slug = "root-host-session-tenant";

    private readonly BaseDomainLoginIntegrationTestsFixture _fixture;

    public BaseDomainRootHostTenantSessionTests(BaseDomainLoginIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantSession_SentToRootHost_IsRefused()
    {
        var user = await _fixture.SeedActiveUserAsync(Slug, "roothostsession@test.onevo.dev", "CorrectPass1!");
        var login = await _fixture.PostLoginAsync(user.Email, "CorrectPass1!");
        var legalCompleted = await _fixture.CompleteLegalAcceptanceAsync(login);
        legalCompleted.StatusCode.Should().Be(HttpStatusCode.Accepted, await legalCompleted.Content.ReadAsStringAsync());
        var exchanged = await _fixture.CompleteTenantSessionExchangeAsync(legalCompleted);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());
        var sessionCookie = _fixture.ExtractCookieValue(exchanged, "crownus_session");

        var root = await GetMeAsync("localhost", sessionCookie);

        var rootBody = await root.Content.ReadAsStringAsync();
        root.StatusCode.Should().Be(HttpStatusCode.Forbidden, rootBody);
        using (var json = JsonDocument.Parse(rootBody))
        {
            json.RootElement.GetProperty("detail").GetString()
                .Should().Be(TenantEnforcementMiddleware.TenantSessionWithoutTenantContextDetail);
        }
        rootBody.Should().NotContain(Slug);

        // Positive control: the same session on its own tenant host still works.
        var tenant = await GetMeAsync($"{Slug}.localhost", sessionCookie);
        tenant.StatusCode.Should().Be(HttpStatusCode.OK, await tenant.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> GetMeAsync(string host, string sessionCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Host = host;
        request.Headers.Add("Cookie", $"crownus_session={sessionCookie}");
        return await _fixture.Client.SendAsync(request);
    }
}
