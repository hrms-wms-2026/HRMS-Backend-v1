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
using Xunit;

namespace ONEVO.Tests.Integration.OrgStructure.Department;

/// <summary>
/// Shared, one-time-per-class setup for DepartmentsIntegrationTests: clones the database, boots
/// the WebApplicationFactory, provisions the two fixture tenants/users used by every [Fact] below.
/// xUnit's IClassFixture constructs this ONCE and disposes it once after every fact in the class
/// has run, instead of IAsyncLifetime's default of once PER fact - previously this class's own
/// InitializeAsync (real WebApplicationFactory host boot + several real bcrypt-hashed HTTP logins)
/// ran 51 times, once per [Fact]. Every [Fact] still creates its own uniquely-named department -
/// only the two base tenants, the two fixture users, and TenantASecondLegalEntityId are shared.
/// One fact (List_Pagination_ReturnsCorrectTotalCountAndPageItems) was changed to create its own
/// fresh legal entity via CreateLegalEntityAsync rather than reuse TenantASecondLegalEntityId,
/// because its exact totalCount==3 assertion is only safe against a legal entity nothing else
/// writes to - every other fact's exact-count assertion here is already scoped to a
/// department/parent the fact itself just created, which stays safe under shared state.
/// </summary>
public sealed class DepartmentsIntegrationTestsFixture : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    public E2ETestFactory Factory => _factory;

    public TenantSession TenantAOwner { get; private set; } = null!;
    public TenantSession TenantBOwner { get; private set; } = null!;
    public TenantSession TenantAOrgReadOnly { get; private set; } = null!;
    public TenantSession TenantANoAccess { get; private set; } = null!;
    public Guid TenantAId { get; private set; }
    public Guid TenantALegalEntityId { get; private set; }
    public Guid TenantASecondLegalEntityId { get; private set; }
    public Guid TenantBLegalEntityId { get; private set; }

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        }
        else
        {
            await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        }
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

        TenantAOwner = await ProvisionAndLoginOwnerAsync("dept-a", "Dept A Co", "owner-a@dept.test");
        TenantBOwner = await ProvisionAndLoginOwnerAsync("dept-b", "Dept B Co", "owner-b@dept.test");

        TenantAId = await GetTenantIdAsync(TenantAOwner.Host);
        TenantALegalEntityId = await GetPrimaryLegalEntityIdAsync(TenantAOwner);
        TenantBLegalEntityId = await GetPrimaryLegalEntityIdAsync(TenantBOwner);
        TenantASecondLegalEntityId = await CreateSecondLegalEntityAsync(TenantAOwner);

        TenantAOrgReadOnly = await SeedAndLoginFixtureUserAsync(
            TenantAId, TenantAOwner.Host, "org-reader@dept-a.test", permissionCodes: ["org:read"], roleName: "Org Reader");
        TenantANoAccess = await SeedAndLoginFixtureUserAsync(
            TenantAId, TenantAOwner.Host, "no-access@dept-a.test", permissionCodes: [], roleName: "No Access");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _environmentScope.DisposeAsync();
    }

    public sealed record TenantSession(string Host, string SessionCookie, string CsrfHeader);

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


    /// <summary>
    /// Seeds a second tenant user directly in the DB with a dedicated role carrying exactly
    /// <paramref name="permissionCodes"/> (empty = zero permissions, mirroring the auto-created,
    /// unassigned "Employee" role every tenant already gets from DefaultRoleSeeder), plus the
    /// LegalAcceptanceRecord rows real invite-acceptance would have written, so the subsequent
    /// real HTTP base-login -> session-exchange completes cleanly instead of hitting a legal
    /// challenge. Only the fixture setup bypasses HTTP; every assertion in this class still runs
    /// the real request through the full pipeline.
    /// </summary>
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
            Description = $"Part 2D fixture role: {roleName}",
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

    public async Task<Guid> GetTenantIdAsync(string host)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var slug = host.Split('.')[0];
        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Slug == slug);
        return tenant.Id;
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(TenantSession session)
    {
        var list = await GetJsonAsync(session, "/api/v1/org/legal-entities");
        var primary = list.EnumerateArray().Single(i => i.GetProperty("isPrimary").GetBoolean());
        return primary.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateSecondLegalEntityAsync(TenantSession session)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host, "/api/v1/org/legal-entities",
            new
            {
                name = "Dept A Second Co",
                companyCode = "DEPTA2",
                registrationNumber = "REG-DEPTA2",
                countryCode = "LKA",
                currencyCode = "LKR"
            },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    public async Task<Guid> CreateLegalEntityAsync(TenantSession session, string name, string companyCode)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host, "/api/v1/org/legal-entities",
            new
            {
                name,
                companyCode,
                registrationNumber = $"REG-{companyCode}",
                countryCode = "LKA",
                currencyCode = "LKR"
            },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    public async Task<JsonElement> CreateDepartmentAsync(TenantSession session, Guid legalEntityId, string name)
    {
        var response = await SendAsync(HttpMethod.Post, session.Host,
            $"/api/v1/org/legal-entities/{legalEntityId}/departments", new { name },
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    public sealed record SeededPosition(Guid Id);

    /// <summary>
    /// Seeds a position directly via the DbContext (there is no public Positions HTTP contract
    /// exercised elsewhere in this fixture), mirroring the Employee-seeding block above - it
    /// already works under FORCE ROW LEVEL SECURITY in this same test class.
    /// </summary>
    public async Task<SeededPosition> CreatePositionAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = $"Head Position {Guid.NewGuid():N}",
            PositionType = ONEVO.Domain.Features.OrgStructure.Entities.Position.TypeUnique,
            MaxOccupancy = 1,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedById = Guid.NewGuid()
        };
        db.Add(position);
        await db.SaveChangesAsync();

        return new SeededPosition(position.Id);
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
                // Schema not created yet; keep polling.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    public async Task<HttpResponseMessage> SendAsync(
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

    public async Task<JsonElement> GetJsonAsync(TenantSession session, string path)
    {
        var response = await SendAsync(HttpMethod.Get, session.Host, path, body: null, cookie: session.SessionCookie);
        var json = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, json.ToString());
        return json;
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

/// <summary>
/// Real HTTPS/API validation for Department Part 2D: every request in this class goes through
/// the full Kestrel TestServer pipeline (Authorize, RequirePermission, MediatR, EF/Postgres/RLS,
/// CSRF middleware) against a real PostgreSQL database - not controller/handler unit tests.
/// Mirrors the LegalEntitiesIntegrationTests convention (two provisioned tenants for cross-tenant
/// isolation). The org:read-only and no-permission fixture users are seeded directly via the DB
/// (there is no public "invite additional employee" endpoint on this tenant's own API yet - only
/// the single owner-invite issued during tenant creation) and then logged in through the real
/// base-domain login -> session-exchange flow, including their own LegalAcceptanceRecord rows so
/// that login completes without a legal challenge (mirroring what invite-acceptance writes for the
/// owner).
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class DepartmentsIntegrationTests : IClassFixture<DepartmentsIntegrationTestsFixture>
{
    private readonly DepartmentsIntegrationTestsFixture _fixture;

    public DepartmentsIntegrationTests(DepartmentsIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithOrgRead_Returns200()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            body: null, cookie: _fixture.TenantAOrgReadOnly.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_WithoutOrgRead_Returns403()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            body: null, cookie: _fixture.TenantANoAccess.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithoutOrgRead_Returns403()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Get Perm Dept");

        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _fixture.TenantANoAccess.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Should Be Blocked Dept" },
            cookie: _fixture.TenantAOrgReadOnly.SessionCookie, csrfToken: _fixture.TenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Update Perm Dept");

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            new { name = "Renamed" },
            cookie: _fixture.TenantAOrgReadOnly.SessionCookie, csrfToken: _fixture.TenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Delete Perm Dept");

        var response = await _fixture.SendAsync(HttpMethod.Delete, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _fixture.TenantAOrgReadOnly.SessionCookie, csrfToken: _fixture.TenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithOrgManage_Returns201()
    {
        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Full Access Create Dept" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // -- CRUD + business rules (Owner, full org:manage) ---------------------

    [Fact]
    public async Task Create_Get_Update_Delete_FullLifecycle()
    {
        var created = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Lifecycle Dept");
        created.GetProperty("name").GetString().Should().Be("Lifecycle Dept");
        created.TryGetProperty("headPositionId", out var headOnCreate).Should().BeTrue();
        headOnCreate.ValueKind.Should().Be(JsonValueKind.Null);

        var id = created.GetProperty("id").GetGuid();

        var get = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            new { name = "Lifecycle Dept Renamed" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await ReadJsonAsync(update);
        updateJson.GetProperty("name").GetString().Should().Be("Lifecycle Dept Renamed");

        var delete = await _fixture.SendAsync(HttpMethod.Delete, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Soft delete only: the row still resolves by id, just IsActive = false.
        var afterDelete = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        afterDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDeleteJson = await ReadJsonAsync(afterDelete);
        afterDeleteJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        // Excluded by default, included only with includeInactive=true.
        var defaultList = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameLegalEntity_Returns409()
    {
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Duplicate Dept Name");

        var duplicate = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Duplicate Dept Name" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameNameInDifferentLegalEntity_IsAllowed()
    {
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Shared Name Dept");

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments",
            new { name = "Shared Name Dept" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_SelfParenting_Returns400()
    {
        // UpdateDepartmentCommandValidator (FluentValidation, runs in the MediatR pipeline
        // before the handler) already rejects ParentDepartmentId == DepartmentId with a
        // validation failure -> 400. UpdateDepartmentCommandHandler.cs:49-50 has its own
        // self-parenting check returning Conflict (409), but the validator's earlier rejection
        // means that handler-level check is unreachable for this exact input - both layers
        // reject self-parenting, the validator's 400 is just the one that actually surfaces.
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Self Parent Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            new { name = "Self Parent Dept", parentDepartmentId = id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ParentInDifferentLegalEntity_Returns404()
    {
        var parentInOtherLegalEntity = await _fixture.CreateDepartmentAsync(
            _fixture.TenantAOwner, _fixture.TenantASecondLegalEntityId, "Parent In Other LE");
        var parentId = parentInOtherLegalEntity.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Child With Wrong Parent LE", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ParentInDifferentTenant_Returns404()
    {
        var parentInOtherTenant = await _fixture.CreateDepartmentAsync(
            _fixture.TenantBOwner, _fixture.TenantBLegalEntityId, "Parent In Other Tenant");
        var parentId = parentInOtherTenant.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Child With Cross Tenant Parent", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_WithHeadPositionId_Returns409_AssignmentDeferredToUpdate()
    {
        // Part 3: a new department has no positions belonging to it yet, so head-position
        // assignment on create is rejected outright (not silently ignored) - see
        // DEPARTMENT_HEAD_POSITION_ASSIGNMENT_REPORT.md. Assign it afterwards through update.
        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Head Position Deferred Dept", headPositionId = Guid.NewGuid() },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // -- Part 3: department head position assignment (update-only) ----------

    [Fact]
    public async Task Update_WithHeadPositionId_AssignsHeadPosition_AndResponseIncludesIt()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Assign Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await _fixture.CreatePositionAsync(_fixture.TenantAId, _fixture.TenantALegalEntityId, departmentId, isActive: true);

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Assign Dept", headPositionId = position.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("headPositionId").GetGuid().Should().Be(position.Id);

        var get = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}");
        get.GetProperty("headPositionId").GetGuid().Should().Be(position.Id);
    }

    [Fact]
    public async Task Update_OmittingHeadPositionId_ClearsPreviouslyAssignedHead()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Clear Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await _fixture.CreatePositionAsync(_fixture.TenantAId, _fixture.TenantALegalEntityId, departmentId, isActive: true);

        var assign = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Clear Dept", headPositionId = position.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        // Full-replace PUT semantics: omitting headPositionId clears it, exactly like sending
        // null would - the request model cannot distinguish the two (see report for rationale).
        var clear = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Clear Dept" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(clear);
        json.GetProperty("headPositionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_HeadPositionId_NotFound_Returns404()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Missing Dept");
        var departmentId = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Missing Dept", headPositionId = Guid.NewGuid() },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_HeadPositionId_Inactive_Returns409()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Inactive Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var position = await _fixture.CreatePositionAsync(_fixture.TenantAId, _fixture.TenantALegalEntityId, departmentId, isActive: false);

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Inactive Dept", headPositionId = position.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherDepartment_Returns409()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Wrong Dept A");
        var departmentId = department.GetProperty("id").GetGuid();
        var otherDepartment = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Wrong Dept B");
        var otherDepartmentId = otherDepartment.GetProperty("id").GetGuid();
        var position = await _fixture.CreatePositionAsync(_fixture.TenantAId, _fixture.TenantALegalEntityId, otherDepartmentId, isActive: true);

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Wrong Dept A", headPositionId = position.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherLegalEntity_Returns404()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Cross LE Dept");
        var departmentId = department.GetProperty("id").GetGuid();
        var deptInOtherLe = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantASecondLegalEntityId, "Head Cross LE Other Dept");
        var positionInOtherLe = await _fixture.CreatePositionAsync(
            _fixture.TenantAId, _fixture.TenantASecondLegalEntityId, deptInOtherLe.GetProperty("id").GetGuid(), isActive: true);

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Cross LE Dept", headPositionId = positionInOtherLe.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_HeadPositionId_FromAnotherTenant_Returns404_RlsIsolationIntact()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Head Cross Tenant Dept");
        var departmentId = department.GetProperty("id").GetGuid();

        // Seeded entirely within tenant B (its own tenantId and legalEntityId). Note this does
        // not isolate tenant scoping from legal-entity scoping - either filter alone would
        // explain the 404, since both belong to tenant B here. Isolating them would require a
        // position row whose tenant_id and legal_entity_id belong to different tenants; that
        // combination was not verified against PositionConfiguration's FK constraints and was
        // deliberately not attempted (see Remaining limitations in the report).
        var tenantBId = await _fixture.GetTenantIdAsync(_fixture.TenantBOwner.Host);
        var positionInOtherTenant = await _fixture.CreatePositionAsync(
            tenantBId, _fixture.TenantBLegalEntityId, departmentId: null, isActive: true);

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}",
            new { name = "Head Cross Tenant Dept", headPositionId = positionInOtherTenant.Id },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -- Code rules + hierarchy safety + archive route -----------------------

    [Fact]
    public async Task Create_WithCode_Returns201_AndCodeIsPreserved()
    {
        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Operations Dept", code = "OPS" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("code").GetString().Should().Be("OPS");
    }

    [Fact]
    public async Task Create_DuplicateCodeCaseInsensitiveInSameLegalEntity_Returns409()
    {
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Original Code Dept", code = "DUPCODE" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Different Name Dept", code = "dupcode" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentLegalEntity_IsAllowed()
    {
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Shared Code Dept A", code = "SHARED" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments",
            new { name = "Shared Code Dept B", code = "SHARED" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_InvalidCodeCharacters_Returns400()
    {
        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Bad Code Dept", code = "bad code!" },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ParentIsInactive_Returns409()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Inactive Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();
        var archiveResponse = await _fixture.SendAsync(HttpMethod.Delete, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var child = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Child Of Inactive Parent");
        var childId = child.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{childId}",
            new { name = "Child Of Inactive Parent", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ParentIsDescendant_Returns409()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Cycle Parent Dept");
        var parentId = parent.GetProperty("id").GetGuid();

        var childResponse = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Cycle Child Dept", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        childResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var childJson = await ReadJsonAsync(childResponse);
        var childId = childJson.GetProperty("id").GetGuid();

        // Attempt to make the parent report to its own child - must be blocked as a cycle.
        var response = await _fixture.SendAsync(HttpMethod.Put, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}",
            new { name = "Cycle Parent Dept", parentDepartmentId = childId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Route_SoftDeactivates_AndListExcludesByDefault()
    {
        var created = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Route Dept");
        var id = created.GetProperty("id").GetGuid();

        var archive = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeFalse();

        var defaultList = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        var inclusiveList = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?includeInactive=true");
        inclusiveList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    // -- Part 3: search, sort, pagination, tree ------------------------------

    [Fact]
    public async Task List_ReturnsOnlyDepartmentsForSelectedLegalEntity()
    {
        var deptInFirstLe = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "List Isolation LE1");
        var deptInSecondLe = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantASecondLegalEntityId, "List Isolation LE2");

        var firstLeList = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments");
        var ids = firstLeList.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(deptInFirstLe.GetProperty("id").GetGuid());
        ids.Should().NotContain(deptInSecondLe.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_Search_ReturnsOnlyMatchingDepartments_ScopedToLegalEntity()
    {
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Search Match Marketing");
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Search NoMatch Finance");

        var response = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?search=marketing");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Search Match Marketing");
        names.Should().NotContain("Search NoMatch Finance");
    }

    [Fact]
    public async Task List_Pagination_ReturnsCorrectTotalCountAndPageItems()
    {
        // Own fresh legal entity, not the shared TenantASecondLegalEntityId - IClassFixture means
        // every other fact's departments would otherwise accumulate here too, breaking the exact
        // totalCount==3 assertion below once fact execution order isn't the one this was written
        // against.
        var legalEntityId = await _fixture.CreateLegalEntityAsync(
            _fixture.TenantAOwner, "Pagination Dept LE", "PGDPLE");

        for (var i = 0; i < 3; i++)
        {
            await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, legalEntityId, $"Page Dept {i}");
        }

        var response = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{legalEntityId}/departments?page=1&pageSize=2");

        response.GetProperty("totalCount").GetInt32().Should().Be(3);
        response.GetProperty("page").GetInt32().Should().Be(1);
        response.GetProperty("pageSize").GetInt32().Should().Be(2);
        response.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task List_TreeView_ReturnsHierarchyForSelectedLegalEntityOnly()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Tree Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Tree Child", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantASecondLegalEntityId, "Other LE Root");

        var response = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?view=tree");

        response.TryGetProperty("treeItems", out var treeItems).Should().BeTrue();
        var parentNode = treeItems.EnumerateArray().Single(n => n.GetProperty("id").GetGuid() == parentId);
        parentNode.GetProperty("children").GetArrayLength().Should().Be(1);
        treeItems.EnumerateArray().Select(n => n.GetProperty("name").GetString()).Should().NotContain("Other LE Root");
    }

    [Fact]
    public async Task List_TreeView_DoesNotExposeTenantId()
    {
        await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Tree No Tenant");

        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?view=tree",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        var text = await response.Content.ReadAsStringAsync();

        text.Should().NotContain("tenantId", "tree responses must not expose the tenant id");
    }

    [Fact]
    public async Task List_InvalidSortBy_Returns400()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?sortBy=nope",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_PageSizeOverMax_Returns400()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments?pageSize=101",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ParentDepartmentIdFilter_ReturnsOnlyDirectChildren()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantASecondLegalEntityId, "Filter Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        var childResponse = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments",
            new { name = "Filter Child", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        var childId = (await ReadJsonAsync(childResponse)).GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments",
            new { name = "Filter Grandchild", parentDepartmentId = childId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var response = await _fixture.GetJsonAsync(_fixture.TenantAOwner,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments?parentDepartmentId={parentId}");

        var names = response.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList();
        names.Should().ContainSingle().Which.Should().Be("Filter Child");
    }

    // -- Cross-tenant / cross-legal-entity isolation -------------------------
    // 404 is the correct "blocked" semantic here (existence-hiding), matching
    // the same convention already established by LegalEntitiesIntegrationTests
    // (GetGeneralSettings_OutOfTenantId_Returns404) - not a weakened check.

    [Fact]
    public async Task Get_CrossTenant_Returns404()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Cross Tenant Dept");

        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _fixture.TenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_CrossTenant_LegalEntityId_Returns404()
    {
        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantBOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            body: null, cookie: _fixture.TenantBOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_CrossLegalEntity_WithinSameTenant_Returns404()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "LE Scoped Dept");

        var response = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantASecondLegalEntityId}/departments/{department.GetProperty("id").GetGuid()}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }


    [Fact]
    public async Task ArchiveCheck_Unauthenticated_Returns401()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Check Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive-check", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Restore_Unauthenticated_Returns401()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Restore Unauth Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/restore", body: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArchiveCheck_Eligible_ReturnsCanArchiveTrue()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Check Eligible");
        var id = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeTrue();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(0);
        json.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ArchiveCheck_WithOrgRead_Returns200()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Check Perm Dept");
        var id = department.GetProperty("id").GetGuid();

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive-check",
            body: null, cookie: _fixture.TenantAOrgReadOnly.SessionCookie, csrfToken: _fixture.TenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchiveCheck_Blocked_ReturnsAccurateCounts_WhenActiveChildExists()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Check Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Archive Check Child", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}/archive-check",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(response);
        json.GetProperty("canArchive").GetBoolean().Should().BeFalse();
        json.GetProperty("blockers").GetProperty("activeSubdepartmentCount").GetInt32().Should().Be(1);
        json.GetProperty("blockers").GetProperty("isUsedAsParent").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveChildExists_Returns409_AndDoesNotDeactivate()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Archive Blocked Child", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var archive = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var afterArchive = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        var afterArchiveJson = await ReadJsonAsync(afterArchive);
        afterArchiveJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Blocked_WhenActiveChildExists_Returns409()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Delete Blocked Parent");
        var parentId = parent.GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Delete Blocked Child", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var delete = await _fixture.SendAsync(HttpMethod.Delete, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Child_WithNoBlockers_Succeeds_ThenRestore_Succeeds()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Archive Then Restore");
        var id = department.GetProperty("id").GetGuid();

        var archive = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restore = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _fixture.SendAsync(HttpMethod.Get, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie);
        var getJson = await ReadJsonAsync(get);
        getJson.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Restore_WithOrgReadOnly_NoOrgManage_Returns403()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Restore Perm Dept");
        var id = department.GetProperty("id").GetGuid();
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);

        var response = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{id}/restore",
            body: null, cookie: _fixture.TenantAOrgReadOnly.SessionCookie, csrfToken: _fixture.TenantAOrgReadOnly.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Restore_Fails_WhenParentIsArchived()
    {
        var parent = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Restore Parent Archived");
        var parentId = parent.GetProperty("id").GetGuid();
        var child = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments",
            new { name = "Restore Child Blocked", parentDepartmentId = parentId },
            cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        var childJson = await ReadJsonAsync(child);
        var childId = childJson.GetProperty("id").GetGuid();

        // Archive child first (no blockers), then the parent (which now has zero active
        // children, so it archives cleanly too).
        await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{childId}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        var archiveParent = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{parentId}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archiveParent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restoreChild = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{childId}/restore",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        restoreChild.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Archive_Blocked_WhenActiveEmployeeExists()
    {
        var department = await _fixture.CreateDepartmentAsync(_fixture.TenantAOwner, _fixture.TenantALegalEntityId, "Has Active Employee");
        var departmentId = department.GetProperty("id").GetGuid();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activeStatus = await db.EmploymentStatuses.SingleAsync(s => s.Code == "active");

            db.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantAId,
                UserId = Guid.NewGuid(),
                LegalEntityId = _fixture.TenantALegalEntityId,
                DepartmentId = departmentId,
                EmployeeNumber = $"E{Guid.NewGuid():N}"[..12],
                FirstName = "Active",
                LastName = "Employee",
                Email = $"{Guid.NewGuid():N}@dept.test",
                EmploymentStatusId = activeStatus.Id,
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            await db.SaveChangesAsync();
        }

        var archive = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}/archive",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        archive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var check = await _fixture.SendAsync(HttpMethod.Post, _fixture.TenantAOwner.Host,
            $"/api/v1/org/legal-entities/{_fixture.TenantALegalEntityId}/departments/{departmentId}/archive-check",
            body: null, cookie: _fixture.TenantAOwner.SessionCookie, csrfToken: _fixture.TenantAOwner.CsrfHeader);
        var checkJson = await ReadJsonAsync(check);
        checkJson.GetProperty("blockers").GetProperty("activeEmployeeCount").GetInt32().Should().Be(1);
        checkJson.GetProperty("blockers").GetProperty("hasActiveEmployees").GetBoolean().Should().BeTrue();
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
