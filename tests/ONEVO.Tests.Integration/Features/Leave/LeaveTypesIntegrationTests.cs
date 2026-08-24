using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.Leave;

[Collection(WebApplicationFactoryCollection.Name)]
public class LeaveTypesIntegrationTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private TenantSession _owner = null!;
    private TenantSession _noManage = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_leave_types_test")
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

        await WaitForSeedersAsync();

        var loginResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        _owner = await ProvisionAndLoginOwnerAsync("leave-a", "Leave A Co", "owner-a@leave.test");
        _tenantId = await GetTenantIdAsync(_owner.Host);
        _noManage = await SeedAndLoginFixtureUserAsync(
            _tenantId, _owner.Host, "reader@leave-a.test", permissionCodes: ["leave:read"], roleName: "Leave Reader");
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
    public async Task Create_AsOwner_Returns200AndPersists()
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Sick Leave", "SICK"),
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("name").GetString().Should().Be("Sick Leave");
        json.GetProperty("code").GetString().Should().Be("SICK");
        json.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Create_WithoutLeaveManage_Returns403()
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Blocked Leave", "BLOCK"),
            cookie: _noManage.SessionCookie, csrfToken: _noManage.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_Unauthenticated_Returns401()
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Anon Leave", "ANON"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_AfterCreate_IncludesTheType()
    {
        var create = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Annual Leave", "ANNUAL"),
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await ReadJsonAsync(create);
        var id = created.GetProperty("id").GetGuid();

        var list = await SendAsync(HttpMethod.Get, _owner.Host, "/api/v1/leave/types",
            body: null, cookie: _owner.SessionCookie);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadJsonAsync(list);
        items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Update_DoesNotChangeCode()
    {
        var create = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Original Name", "ORIG"),
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        var created = await ReadJsonAsync(create);
        var id = created.GetProperty("id").GetGuid();

        var update = await SendAsync(HttpMethod.Put, _owner.Host, $"/api/v1/leave/types/{id}",
            new
            {
                name = "Renamed Leave",
                description = "updated",
                category = "custom",
                isPaid = true,
                requiresApproval = true,
                requiresDocument = false,
                documentRequiredAfterDays = (int?)null,
                acceptedDocumentTypes = Array.Empty<string>(),
                maxConsecutiveDays = (int?)null,
                defaultDaysPerYear = 12m,
                carryForwardAllowed = false,
                maxCarryForwardDays = (decimal?)null,
                carryForwardExpiryMonths = (int?)null,
                proRataForNewJoiners = false,
                applicableGender = "all",
                minimumNoticeDays = 0
            },
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(update);
        json.GetProperty("name").GetString().Should().Be("Renamed Leave");
        json.GetProperty("code").GetString().Should().Be("ORIG");
    }

    [Fact]
    public async Task Deactivate_HidesFromDefaultList()
    {
        var create = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            CreateBody("Temp Leave", "TEMP"),
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        var id = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var deactivate = await SendAsync(HttpMethod.Post, _owner.Host, $"/api/v1/leave/types/{id}/deactivate",
            body: null, cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await SendAsync(HttpMethod.Get, _owner.Host, "/api/v1/leave/types",
            body: null, cookie: _owner.SessionCookie);
        var items = await ReadJsonAsync(list);
        items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);
    }

    private static object CreateBody(string name, string code) => new
    {
        name,
        code,
        description = "test type",
        category = "custom",
        isPaid = true,
        requiresApproval = true,
        requiresDocument = false,
        documentRequiredAfterDays = (int?)null,
        acceptedDocumentTypes = Array.Empty<string>(),
        maxConsecutiveDays = (int?)null,
        defaultDaysPerYear = 10m,
        carryForwardAllowed = false,
        maxCarryForwardDays = (decimal?)null,
        carryForwardExpiryMonths = (int?)null,
        proRataForNewJoiners = false,
        applicableGender = "all",
        minimumNoticeDays = 0
    };

    private sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

    private async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
    {
        const string ownerPassword = "OwnerPass@2026!";
        var host = $"{slug}.localhost";

        var createBody = new
        {
            company_name = companyName,
            slug,
            industry_profile = "technology",
            company_size_range = "11-50",
            legal_entity_name = companyName,
            registration_number = $"PV-{slug}",
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
                email = ownerEmail,
                first_name = "Test",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendAsync(HttpMethod.Post, host,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = ownerPassword,
                confirm_password = ownerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmResponse = await SendAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return await LoginViaBaseHostAsync(host, ownerEmail, ownerPassword);
    }

    private async Task<TenantSession> LoginViaBaseHostAsync(string host, string email, string password)
    {
        const string baseHost = "localhost";
        var loginResponse = await SendAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email, password });
        var loginJson = await ReadJsonAsync(loginResponse);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, loginJson.ToString());
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange",
            new { code = exchangeCode });
        var exchangeJson = await ReadJsonAsync(exchangeResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, exchangeJson.ToString());
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);
        return new TenantSession(host, sessionCookie, csrfHeader);
    }

    private async Task<TenantSession> SeedAndLoginFixtureUserAsync(
        Guid tenantId, string host, string email, IReadOnlyList<string> permissionCodes, string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        var userId = Guid.NewGuid();
        db.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email,
            FirstName = "Fixture",
            LastName = roleName,
            PasswordHash = hasher.Hash(FixtureUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = userId
        });

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = roleName,
            Description = $"Leave fixture role: {roleName}",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });

        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenantId, RoleId = roleId, PermissionId = permission.Id });
        }

        db.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedBy = userId });

        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "terms", DocumentVersion = "1.0", Decision = "accepted",
            Required = true, DecidedAt = now, Source = "test-seed"
        });
        db.Add(new LegalAcceptanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            DocumentType = "privacy_notice", DocumentVersion = "1.0", Decision = "acknowledged",
            Required = true, DecidedAt = now, Source = "test-seed"
        });

        await db.SaveChangesAsync();

        return await LoginViaBaseHostAsync(host, email, FixtureUserPassword);
    }

    private async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId != "tenant_owner_invite")
                    continue;
                if (!string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (template.Data.TryGetProperty("invite_token", out var token))
                    return token.GetString();
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>()
                    .AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady)
                    return;
            }
            catch
            {
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
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
