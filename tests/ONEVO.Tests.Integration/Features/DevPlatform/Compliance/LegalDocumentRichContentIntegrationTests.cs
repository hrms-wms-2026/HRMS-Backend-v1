using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.DevPlatform.Compliance;

/// <summary>
/// End-to-end legal document rich content flow against a real PostgreSQL database:
/// admin creates a draft, publishes it (archiving the prior published bootstrap row),
/// the public content endpoint serves it with no auth/tenant context, a tenant owner's
/// pending-legal response carries content_endpoint/content_hash, and publishing a new
/// version forces re-acceptance on the next login.
///
/// Database resolution: set ONEVO_TEST_DB to a PostgreSQL connection string to run
/// against a local server (no Docker needed); otherwise a Testcontainers instance is
/// started. Mirrors the harness in E2E/TenantProvisioningE2ETests.cs.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class LegalDocumentRichContentIntegrationTests : IAsyncLifetime
{
    private const string Slug = "legal-content-it";
    private const string TenantHost = Slug + ".localhost";
    private const string AdminHost = "admin.localhost";
    private const string BaseHost = "localhost";
    private const string OwnerEmail = "owner@legal-content-it.test";
    private const string OwnerPassword = "OwnerPass@2026!";

    /// <summary>Seeded by SeedPhaseOnePlanModules / model HasData.</summary>
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_legal_content_it")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var cookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = cookies["admin_csrf"];
        _adminCookie = $"admin_session={cookies["admin_session"]}";
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Admin_Can_Create_Publish_And_Publicly_Read_A_Legal_Document_Version()
    {
        // 1. Create a draft.
        var createBody = new
        {
            document_type = "terms",
            version = "9.9-it",
            title = "Integration Test Terms",
            content_json = new { type = "doc", content = Array.Empty<object>() },
            content_html = "<h1>Integration Test Terms</h1><p>Body for the integration test.</p>",
            content_text = "Integration Test Terms\n\nBody for the integration test.",
            is_required = true,
            block_scope = "dashboard"
        };

        var createResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/legal-document-versions",
            createBody, cookie: _adminCookie, csrfToken: _adminCsrfToken);
        var created = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK, created.ToString());
        created.GetProperty("status").GetString().Should().Be("draft");
        var draftContentHash = created.GetProperty("content_hash").GetString();
        draftContentHash.Should().NotBeNullOrWhiteSpace(
            "content_hash must be computed server-side, never accepted from the request body");
        var draftId = created.GetProperty("id").GetGuid();

        // 2. Publish it - this must archive the bootstrap terms/1.0 row (partial unique index).
        var publishResponse = await SendAsync(HttpMethod.Post, AdminHost,
            $"/admin/v1/legal-document-versions/{draftId}/publish",
            new { publish_reason = "Integration test baseline" },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        var published = await ReadJsonAsync(publishResponse);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK, published.ToString());
        published.GetProperty("status").GetString().Should().Be("published");
        published.GetProperty("published_at").GetDateTimeOffset().Should().NotBe(default);
        published.GetProperty("published_by_id").ValueKind.Should().NotBe(JsonValueKind.Null);

        var bootstrapList = await GetJsonAsync(AdminHost, "/admin/v1/legal-document-versions?document_type=terms",
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        var bootstrapRow = bootstrapList.EnumerateArray()
            .Single(v => v.GetProperty("version").GetString() == "1.0");
        bootstrapRow.GetProperty("status").GetString().Should().Be("archived",
            "publishing a new version must archive the prior published version for the same document_type");

        // 3. Public read with NO auth header and NO tenant host resolution required.
        var publicRead = await SendAsync(HttpMethod.Get, BaseHost, "/api/v1/legal/documents/terms/9.9-it", body: null);
        var publicBody = await ReadJsonAsync(publicRead);
        publicRead.StatusCode.Should().Be(HttpStatusCode.OK, publicBody.ToString());
        publicBody.GetProperty("content_html").GetString().Should().Be(createBody.content_html);
        publicBody.GetProperty("content_hash").GetString().Should().Be(draftContentHash);
        publicBody.TryGetProperty("tenant_id", out _).Should().BeFalse();
        publicBody.TryGetProperty("user_id", out _).Should().BeFalse();

        // 4. Current-required endpoint includes it.
        var current = await SendAsync(HttpMethod.Get, BaseHost, "/api/v1/legal/documents/current", body: null);
        var currentBody = await ReadJsonAsync(current);
        current.StatusCode.Should().Be(HttpStatusCode.OK, currentBody.ToString());
        currentBody.EnumerateArray().Should().Contain(
            d => d.GetProperty("document_type").GetString() == "terms"
                 && d.GetProperty("version").GetString() == "9.9-it");
    }

    [Fact]
    public async Task Publishing_A_New_Terms_Version_Forces_Reacceptance_On_Next_Login()
    {
        // -- Provision a tenant owner and get them to an authenticated session, accepting
        //    the then-current required legal versions during invite acceptance. --
        var tenantId = await CreateTenantAsync();
        var inviteToken = await WaitForInviteTokenAsync();
        inviteToken.Should().NotBeNullOrEmpty();

        var (acceptBody, _) = await PostJsonAsync(TenantHost, $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = OwnerPassword,
                confirm_password = OwnerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptBody.GetProperty("authenticated").GetBoolean().Should().BeTrue();

        var confirm = await SendAsync(HttpMethod.Patch, AdminHost, $"/admin/v1/tenants/{tenantId}/provision/confirm",
            new { confirm = true }, cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var firstLogin = await SendAsync(HttpMethod.Post, BaseHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        var firstLoginBody = await ReadJsonAsync(firstLogin);
        firstLogin.StatusCode.Should().Be(HttpStatusCode.OK, firstLoginBody.ToString());
        firstLoginBody.GetProperty("authenticated").GetBoolean().Should().BeTrue(
            "the owner already accepted the current required versions during invite completion");

        // -- Now publish a new terms draft, superseding terms/1.0. --
        var newDraft = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/legal-document-versions",
            new
            {
                document_type = "terms",
                version = "2.0-it",
                title = "Updated Integration Test Terms",
                content_json = new { type = "doc", content = Array.Empty<object>() },
                content_html = "<h1>Updated Terms</h1><p>Updated body.</p>",
                content_text = "Updated Terms\n\nUpdated body.",
                is_required = true,
                block_scope = "dashboard"
            },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        var newDraftBody = await ReadJsonAsync(newDraft);
        newDraft.StatusCode.Should().Be(HttpStatusCode.OK, newDraftBody.ToString());
        var newDraftId = newDraftBody.GetProperty("id").GetGuid();
        var newContentHash = newDraftBody.GetProperty("content_hash").GetString();

        var newPublish = await SendAsync(HttpMethod.Post, AdminHost,
            $"/admin/v1/legal-document-versions/{newDraftId}/publish",
            new { publish_reason = "Integration test v2" },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        newPublish.StatusCode.Should().Be(HttpStatusCode.OK);

        // -- The owner logs in again: now pending on terms/2.0-it, with content_endpoint
        //    and content_hash present so the exact version can be read before accepting. --
        var secondLoginResponse = await SendAsync(HttpMethod.Post, BaseHost, "/api/v1/auth/login",
            new { email = OwnerEmail, password = OwnerPassword });
        var secondLoginBody = await ReadJsonAsync(secondLoginResponse);
        secondLoginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, secondLoginBody.ToString());
        secondLoginBody.GetProperty("legal_acceptance_required").GetBoolean().Should().BeTrue();

        var pendingDocs = secondLoginBody.GetProperty("pending_legal_documents");
        var pendingTerms = pendingDocs.EnumerateArray()
            .Single(d => d.GetProperty("document_type").GetString() == "terms");
        pendingTerms.GetProperty("version").GetString().Should().Be("2.0-it");
        pendingTerms.GetProperty("content_endpoint").GetString().Should().Be("/api/v1/legal/documents/terms/2.0-it");
        pendingTerms.GetProperty("content_hash").GetString().Should().Be(newContentHash);

        var cookies = ParseSetCookies(secondLoginResponse);
        cookies.Should().ContainKey("onevo_legal_pending");
        cookies.Should().ContainKey("onevo_legal_csrf");
        var legalCsrfHeader = Uri.UnescapeDataString(cookies["onevo_legal_csrf"]);

        // -- Read the exact pending content via the endpoint the pending response points to,
        //    before ever accepting it. --
        var pendingContentRead = await SendAsync(HttpMethod.Get, BaseHost, pendingTerms.GetProperty("content_endpoint").GetString()!, body: null);
        var pendingContentBody = await ReadJsonAsync(pendingContentRead);
        pendingContentRead.StatusCode.Should().Be(HttpStatusCode.OK);
        pendingContentBody.GetProperty("content_hash").GetString().Should().Be(newContentHash);

        // -- Accept the new version; a session must now be issued. --
        var completeLoginResponse = await SendAsync(
            HttpMethod.Post, BaseHost, "/api/v1/legal/acceptances/complete-login",
            new
            {
                acceptances = new[]
                {
                    new { document_type = "terms", version = "2.0-it", decision = "accepted" }
                }
            },
            cookie: $"onevo_legal_pending={cookies["onevo_legal_pending"]}",
            csrfToken: legalCsrfHeader);
        var completeLoginBody = await ReadJsonAsync(completeLoginResponse);
        completeLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK, completeLoginBody.ToString());
        completeLoginBody.GetProperty("authenticated").GetBoolean().Should().BeTrue();
    }

    // -- Flow steps --

    private async Task<Guid> CreateTenantAsync()
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = new
        {
            company_name = "Legal Content IT Company",
            slug = Slug,
            industry_profile = "technology",
            company_size_range = "51-200",
            legal_entity_name = "Legal Content IT (Pvt) Ltd",
            registration_number = "PV88888",
            country = "LK",
            timezone = "Asia/Colombo",
            currency = "LKR",
            subscription = new
            {
                plan_id = SeededPlanId,
                billing_cycle = "monthly",
                commercial_model = "standard"
            },
            owner_invite = new
            {
                email = OwnerEmail,
                first_name = "Legal",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var response = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", body,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: idempotencyKey);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, json.ToString());
        return json.GetProperty("tenantId").GetGuid();
    }

    private async Task<string?> WaitForInviteTokenAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var token = _email.LastInviteToken();
            if (!string.IsNullOrEmpty(token))
                return token;
            await Task.Delay(250);
        }
        return null;
    }

    // -- HTTP helpers (mirrors E2E/TenantProvisioningE2ETests.cs) --

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string host,
        string path,
        object? body,
        string? cookie = null,
        string? csrfToken = null,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _client.SendAsync(request);
    }

    private async Task<JsonElement> GetJsonAsync(string host, string path, string? cookie = null, string? csrfToken = null)
    {
        var response = await SendAsync(HttpMethod.Get, host, path, body: null, cookie: cookie, csrfToken: csrfToken);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return json;
    }

    private async Task<(JsonElement Body, Dictionary<string, string> Cookies)> PostJsonAsync(
        string host, string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, host, path, body);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return (json, ParseSetCookies(response));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text)
            ? default
            : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return cookies;

        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var idx = pair.IndexOf('=');
            if (idx > 0)
                cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }

        return cookies;
    }
}
