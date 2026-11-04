using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Real-pipeline proof of the tenant session/CSRF cookie lifecycle: the session cookie is persistent
/// like its CSRF cookie, an ended session answers 401 (never a CSRF 403), and logging in again while
/// still holding a live session yields a fresh session whose CSRF token matches.
/// Kept in its own class (own fixture, so its own in-memory login rate limiter) - adding these
/// logins to BaseDomainLoginIntegrationTests would exceed the 20-per-IP login limit there.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class TenantSessionCookieLifecycleIntegrationTests : IClassFixture<BaseDomainLoginIntegrationTestsFixture>
{
    private readonly BaseDomainLoginIntegrationTestsFixture _fixture;

    public TenantSessionCookieLifecycleIntegrationTests(BaseDomainLoginIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantSession_CookieIsPersistent_AndRevokedSessionWithOldCsrf_Returns401NotCsrf403()
    {
        var user = await _fixture.SeedActiveUserAsync(
            "stale-csrf-tenant", "stalecsrf@test.onevo.dev", "CorrectPass1!");

        var response = await _fixture.PostLoginAsync(user.Email, "CorrectPass1!");
        var legalCompleted = await _fixture.CompleteLegalAcceptanceAsync(response);
        var exchanged = await _fixture.CompleteTenantSessionExchangeAsync(legalCompleted);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());

        // onevo_session must be persistent like onevo_csrf; a session-only onevo_session is dropped
        // on browser restart while onevo_csrf lingers, forcing a re-login on a live 30-day session.
        var exchangeCookies = exchanged.Headers.GetValues("Set-Cookie").ToList();
        exchangeCookies.Should().Contain(c =>
            c.StartsWith("onevo_session=", StringComparison.Ordinal) &&
            c.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        var sessionCookie = _fixture.ExtractCookieValue(exchanged, "onevo_session");
        var csrfCookie = _fixture.ExtractCookieValue(exchanged, "onevo_csrf");

        HttpRequestMessage Logout()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            request.Headers.Host = "stale-csrf-tenant.localhost";
            request.Headers.Add("Cookie", $"onevo_session={sessionCookie}; onevo_csrf={csrfCookie}");
            request.Headers.Add("X-CSRF-Token", csrfCookie);
            return request;
        }

        using (var first = Logout())
            (await _fixture.Client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same (now revoked) cookie pair again: must be 401 so the SPA returns to login, not a
        // CSRF 403 that it shows as a permission error.
        using var second = Logout();
        (await _fixture.Client.SendAsync(second)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReLogin_WhileStillHoldingValidSession_IssuesFreshSessionWhoseCsrfMatches()
    {
        var user = await _fixture.SeedActiveUserAsync(
            "relogin-tenant", "relogin@test.onevo.dev", "CorrectPass1!");

        var first = await _fixture.CompleteTenantSessionExchangeAsync(
            await _fixture.CompleteLegalAcceptanceAsync(
                await _fixture.PostLoginAsync(user.Email, "CorrectPass1!")));
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var oldSession = _fixture.ExtractCookieValue(first, "onevo_session");
        var oldCsrf = _fixture.ExtractCookieValue(first, "onevo_csrf");

        // Log in again from the same browser without logging out (second tab, expired UI state, ...):
        // the tenant host receives the session-exchange with the still-valid old cookie pair.
        var second = await _fixture.CompleteTenantSessionExchangeAsync(
            await _fixture.PostLoginAsync(user.Email, "CorrectPass1!"),
            $"onevo_session={oldSession}; onevo_csrf={oldCsrf}");
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var newSession = _fixture.ExtractCookieValue(second, "onevo_session");
        var newCsrf = _fixture.ExtractCookieValue(second, "onevo_csrf");
        newCsrf.Should().NotBe(oldCsrf);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Host = "relogin-tenant.localhost";
        me.Headers.Add("Cookie", $"onevo_session={newSession}; onevo_csrf={newCsrf}");
        (await _fixture.Client.SendAsync(me)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The new CSRF cookie must match the new session - a reused old session row kept the old
        // csrf_token_hash, so every mutation (including logout) failed with a CSRF 403.
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Host = "relogin-tenant.localhost";
        logout.Headers.Add("Cookie", $"onevo_session={newSession}; onevo_csrf={newCsrf}");
        logout.Headers.Add("X-CSRF-Token", newCsrf);
        (await _fixture.Client.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The superseded session must not stay usable.
        using var oldMe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        oldMe.Headers.Host = "relogin-tenant.localhost";
        oldMe.Headers.Add("Cookie", $"onevo_session={oldSession}");
        (await _fixture.Client.SendAsync(oldMe)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
