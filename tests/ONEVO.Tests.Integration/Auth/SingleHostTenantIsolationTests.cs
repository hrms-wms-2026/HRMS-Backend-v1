using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Tenancy;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// SingleHost test mode takes the tenant from the X-Crownus-Tenant header. The header is
/// attacker-controlled, so the session cookie (whose token carries tenant_id) must still win:
/// a session for tenant A sent with header tenant B is refused by TenantEnforcementMiddleware
/// and returns no tenant B data. Requires Docker (Testcontainers SQL Server).
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SingleHostTenantIsolationTests : IAsyncLifetime
{
    private const string TenantA = "acme";
    private const string TenantB = "company1";
    private const string Password = "CorrectPass1!";
    private const string Host = "localhost";

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private BaseDomainLoginTestFactory _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _baseFactory = new BaseDomainLoginTestFactory(connectionString);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenancy:Mode"] = "SingleHost",
                    ["Tenancy:RootDomain"] = Host
                })));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{Host}"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task TokenForTenantA_WithHeaderTenantA_Succeeds()
    {
        var sessionCookie = await SignInAsync("iso-a-ok@test.onevo.dev");

        var me = await GetMeAsync(sessionCookie, TenantA);

        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync());
        (await me.Content.ReadAsStringAsync()).Should().Contain($"\"slug\":\"{TenantA}\"");
    }

    [Fact]
    public async Task TokenForTenantA_WithHeaderTenantB_IsRefused()
    {
        var sessionCookie = await SignInAsync("iso-a-cross@test.onevo.dev");
        await SeedTenantAsync(TenantB);

        var me = await GetMeAsync(sessionCookie, TenantB);

        me.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        var body = await me.Content.ReadAsStringAsync();
        body.Should().NotContain(TenantB);
        body.Should().NotContain($"\"slug\":\"{TenantB}\"");
    }

    // ---- helpers ----

    private async Task<HttpResponseMessage> GetMeAsync(string sessionCookie, string headerTenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Add(TenancyHeaders.Tenant, headerTenant);
        request.Headers.Add("Cookie", $"crownus_session={sessionCookie}");
        return await _client.SendAsync(request);
    }

    /// <summary>
    /// Seeds a user in tenant A, then runs the real base login -> legal acceptance -> session
    /// exchange flow in SingleHost mode and returns the crownus_session cookie value.
    /// </summary>
    private async Task<string> SignInAsync(string email)
    {
        await SeedTenantAsync(TenantA);
        await SeedUserAsync(TenantA, email);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Content = JsonContent.Create(new { email, password = Password });
        var login = await _client.SendAsync(loginRequest);
        login.StatusCode.Should().Be(HttpStatusCode.Accepted, await login.Content.ReadAsStringAsync());

        var legal = await CompleteLegalAcceptanceAsync(login);
        legal.StatusCode.Should().Be(HttpStatusCode.Accepted, await legal.Content.ReadAsStringAsync());

        var exchanged = await ExchangeSessionAsync(legal);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK, await exchanged.Content.ReadAsStringAsync());
        return ExtractCookieValue(exchanged, "crownus_session");
    }

    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage prior)
    {
        var legalPending = ExtractCookieValue(prior, "crownus_legal_pending");
        var legalCsrf = ExtractCookieValue(prior, "crownus_legal_csrf");
        using var document = JsonDocument.Parse(await prior.Content.ReadAsStringAsync());
        var continueUrl = new Uri(document.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"crownus_legal_pending={legalPending}; crownus_legal_csrf={legalCsrf}");
        request.Headers.Add("X-CSRF-Token", legalCsrf);
        request.Content = JsonContent.Create(new
        {
            acceptances = new[]
            {
                new { document_type = "terms", version = "1.0", decision = "accepted" },
                new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
            }
        });
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ExchangeSessionAsync(HttpResponseMessage prior)
    {
        using var document = JsonDocument.Parse(await prior.Content.ReadAsStringAsync());
        var continueUrl = new Uri(document.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(continueUrl.Query);
        var code = query["code"].ToString();
        // SingleHost hands the tenant over as ?tenant=<slug> instead of a subdomain.
        query["tenant"].ToString().Should().Be(TenantA);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session-exchange");
        request.Headers.Add(TenancyHeaders.Tenant, TenantA);
        request.Content = JsonContent.Create(new { code });
        return await _client.SendAsync(request);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Enumerable.Empty<string>();
        foreach (var cookie in setCookies)
        {
            var parts = cookie.Split(';')[0].Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }

        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    private async Task SeedTenantAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The shared migrated template may already contain this slug; make sure it is Active.
        var existing = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Slug == slug);
        if (existing is not null)
        {
            existing.Status = TenantStatus.Active;
            await db.SaveChangesAsync();
            return;
        }

        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedUserAsync(string slug, string email)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Slug == slug);

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 12),
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
}
