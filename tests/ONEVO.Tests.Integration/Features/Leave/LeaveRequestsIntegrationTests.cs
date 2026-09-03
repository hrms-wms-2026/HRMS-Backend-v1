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
public class LeaveRequestsIntegrationTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private const string OwnerEmail = "owner-a@leave-req.test";
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
                .WithDatabase("onevo_leave_requests_test")
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

        _owner = await ProvisionAndLoginOwnerAsync("leave-req", "Leave Req Co", OwnerEmail);
        _tenantId = await GetTenantIdAsync(_owner.Host);
        _noManage = await SeedAndLoginFixtureUserAsync(
            _tenantId, _owner.Host, "reader@leave-req.test", permissionCodes: ["leave:read"], roleName: "Leave Reader");
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
    public async Task SubmitOwnRequest_ReservesPaidPendingDaysAndListsMine()
    {
        var leaveTypeId = await CreateLeaveTypeAsync("Annual Leave", "AL", requiresApproval: false);
        var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);
        await CreatePolicyAsync("Annual Policy", leaveTypeId, legalEntityId, 17.5m);
        var employeeId = await EnsureEmployeeInLegalEntityAsync(_tenantId, legalEntityId);

        var generate = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/entitlements/generate",
            new { year = 2026, legalEntityId },
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        generate.StatusCode.Should().Be(HttpStatusCode.OK);

        var submit = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/requests",
            new
            {
                leaveTypeId,
                startDate = "2026-09-14",
                endDate = "2026-09-14",
                halfDayPeriod = (string?)null,
                reason = "Family event",
                fileRecordIds = Array.Empty<Guid>()
            },
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        submit.StatusCode.Should().Be(HttpStatusCode.OK, await submit.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(submit);
        json.GetProperty("status").GetString().Should().Be("pending");
        json.GetProperty("paidDays").GetDecimal().Should().Be(1m);
        json.GetProperty("unpaidDays").GetDecimal().Should().Be(0m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entitlement = await db.LeaveEntitlements.SingleAsync(x =>
                x.TenantId == _tenantId && x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId);
            entitlement.PendingDays.Should().Be(1m);
            entitlement.UsedDays.Should().Be(0m);
        }

        var mine = await SendAsync(HttpMethod.Get, _owner.Host, "/api/v1/leave/requests/my",
            body: null,
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await ReadJsonAsync(mine);
        list.EnumerateArray().Should().Contain(x => x.GetProperty("leaveTypeId").GetGuid() == leaveTypeId);
    }

    [Fact]
    public async Task OnBehalf_WithoutLeaveManage_Returns403()
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/requests/on-behalf",
            new
            {
                employeeId = Guid.NewGuid(),
                leaveTypeId = Guid.NewGuid(),
                startDate = "2026-09-14",
                endDate = "2026-09-14"
            },
            cookie: _noManage.SessionCookie, csrfToken: _noManage.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_OverlappingDates_Returns409()
    {
        var leaveTypeId = await CreateLeaveTypeAsync("Sick Leave", "SICK", requiresApproval: false);
        var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);
        await CreatePolicyAsync("Sick Policy", leaveTypeId, legalEntityId, 10m);
        await EnsureEmployeeInLegalEntityAsync(_tenantId, legalEntityId);
        var generate = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/entitlements/generate",
            new { year = 2026, legalEntityId },
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        generate.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new
        {
            leaveTypeId,
            startDate = "2026-09-15",
            endDate = "2026-09-15",
            halfDayPeriod = (string?)null,
            reason = (string?)null,
            fileRecordIds = Array.Empty<Guid>()
        };
        var first = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/requests", body,
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var second = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/requests", body,
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<Guid> CreateLeaveTypeAsync(string name, string code, bool requiresApproval = true)
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
            new
            {
                name,
                code,
                description = "integration fixture type",
                category = "custom",
                isPaid = true,
                requiresApproval,
                requiresDocument = false,
                documentRequiredAfterDays = (int?)null,
                acceptedDocumentTypes = Array.Empty<string>(),
                maxConsecutiveDays = (int?)null,
                defaultDaysPerYear = 20m,
                carryForwardAllowed = true,
                maxCarryForwardDays = 5m,
                carryForwardExpiryMonths = 3,
                proRataForNewJoiners = true,
                applicableGender = "all",
                minimumNoticeDays = 0
            },
            cookie: _owner.SessionCookie,
            csrfToken: _owner.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task CreatePolicyAsync(string name, Guid leaveTypeId, Guid legalEntityId, decimal annualDays)
    {
        var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
            CreatePolicyBody(name, leaveTypeId, legalEntityId, confirm: false, annualDays),
            cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> EnsureEmployeeInLegalEntityAsync(Guid tenantId, Guid legalEntityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The submit-for-self path (POST /api/v1/leave/requests) resolves the CURRENT USER to an
        // employee row, so the fixture employee must be linked to _owner's users row - a random
        // UserId leaves _owner employee-less and every own-request submit 404s.
        var ownerUserId = await db.Users
            .Where(u => u.TenantId == tenantId && u.Email == OwnerEmail)
            .Select(u => u.Id)
            .SingleAsync();

        var existing = await db.Employees.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.LegalEntityId == legalEntityId);
        if (existing is not null)
        {
            var dirty = false;
            if (existing.HireDate.Year < 1900)
            {
                existing.HireDate = new DateOnly(2024, 1, 1);
                dirty = true;
            }
            if (existing.UserId != ownerUserId)
            {
                existing.UserId = ownerUserId;
                dirty = true;
            }
            if (dirty)
                await db.SaveChangesAsync();
            return existing.Id;
        }

        var employeeId = Guid.NewGuid();
        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = ownerUserId,
            EmployeeNumber = "EMP-LEAVE-001",
            FirstName = "Priya",
            LastName = "Nair",
            Email = "priya.leave@test.dev",
            LegalEntityId = legalEntityId,
            HireDate = new DateOnly(2024, 1, 1),
            EmploymentStatusId = 1
        });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private async Task<Guid> GetPrimaryLegalEntityIdAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LegalEntities
            .Where(x => x.TenantId == tenantId && x.IsPrimary)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private static object CreatePolicyBody(
        string name, Guid leaveTypeId, Guid legalEntityId, bool confirm, decimal annualEntitlementDays = 20m) => new
    {
        name,
        description = "integration fixture policy",
        country = "LK",
        jobLevel = (string?)null,
        accrualMethod = "annual",
        accrualStart = "immediately",
        accrualAfterNMonths = (int?)null,
        prorationMethod = "calendar_days",
        probationRestriction = false,
        minimumTenureMonths = 0,
        firstYearReducedPercent = (decimal?)null,
        minimumNoticeDays = 7,
        maxConsecutiveDays = 14,
        minDaysPerRequest = 0.5m,
        maxTeamAbsencePercent = 20m,
        approvalMode = "any_one",
        effectiveFrom = "2026-01-01",
        leaveTypes = new[]
        {
            new
            {
                leaveTypeId,
                annualEntitlementDays,
                monthlyAccrualDays = (decimal?)null,
                carryForwardMaxDays = 5m,
                carryForwardExpiryMonths = 3
            }
        },
        blackoutPeriods = new[]
        {
            new
            {
                startDate = "2026-12-24",
                endDate = "2026-12-26",
                reason = "Peak closure"
            }
        },
        legalEntityIds = new[] { legalEntityId },
        confirmReplaceExistingLegalEntityAssignments = confirm
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
