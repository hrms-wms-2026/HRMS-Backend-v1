using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.TimeAttendance;

/// <summary>
/// Focused PostgreSQL integration coverage for the Attendance Correction approval-snapshot
/// contract (ApprovalRequired persisted once at creation time, never re-derived from status) and
/// for the corrective FK-support-index migration (20260824180229_AddAttendanceCorrectionForeignKeyIndexes).
///
/// This intentionally does NOT replicate the full Legal Entity fixture's ~1200 lines of
/// company/department/position CRUD coverage. It reuses only the minimal, already-proven
/// tenant-provisioning and direct-DbContext employee-seeding helpers from that fixture (see
/// LegalEntitiesIntegrationTests/LeaveTypesIntegrationTests) because building a fresh
/// tenant/legal-entity/user/employee by hand risks silently missing one of the many invariants
/// (normalized email generation, seeded reference data, RBAC wiring) the real provisioning
/// endpoint already enforces correctly.
///
/// Attendance-correction rows themselves are seeded directly through a DbContext resolved from
/// the WebApplicationFactory's DI container (which - see E2ETestFactory.ConfigureWebHost - is
/// wired to the raw Testcontainers superuser connection, bypassing RLS) rather than driven
/// through AttendanceCorrectionWorkflow.RequestAsync. That workflow's approval-routing/schedule/
/// clock-in-policy decision logic is already covered by AttendanceCorrectionNotificationTests
/// (unit, with fakes); what is NOT covered anywhere else is whether the real Postgres column
/// round-trips correctly, whether the API response layer reads the stored value rather than
/// deriving it, and whether RLS actually blocks cross-tenant access - that is this class's job.
///
/// Database resolution mirrors every other class in this suite: set ONEVO_TEST_DB to run against
/// a local PostgreSQL server with no Docker; otherwise a Testcontainers instance is started.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class AttendanceCorrectionsIntegrationTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private const string FixtureUserPassword = "Password123!";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private string _connectionString = null!;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private TenantSession _ownerA = null!;
    private TenantSession _requesterA = null!;
    private TenantSession _requesterB = null!;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _legalEntityAId;
    private Guid _legalEntityBId;
    private Guid _requesterAEmployeeId;
    private Guid _requesterAUserId;
    private Guid _requesterBEmployeeId;
    private Guid _requesterBUserId;
    private Guid _reviewerAUserId;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_attendance_corrections_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(_connectionString);

        // AdminTestFactory/IntegrationDatabaseBootstrap migrates as the Testcontainers superuser
        // (see E2ETestFactory.ConfigureWebHost), not as onevo_migrator, so the production
        // ALTER DEFAULT PRIVILEGES ... TO onevo_app mechanism (ops/postgres/local-bootstrap-roles.sql)
        // never fires here and onevo_app has no grants on any table by default in this suite. The
        // CrossTenant_* tests below are the only tests in this class that connect as onevo_app
        // (to exercise real RLS), so they need this table's privileges granted explicitly.
        await using (var adminConnection = new NpgsqlConnection(_connectionString))
        {
            await adminConnection.OpenAsync();
            await using var grantCommand = adminConnection.CreateCommand();
            grantCommand.CommandText = "GRANT SELECT, INSERT, UPDATE, DELETE ON attendance_corrections TO onevo_app;";
            await grantCommand.ExecuteNonQueryAsync();
        }

        _environmentScope = new IntegrationTestEnvironmentScope(_connectionString);

        _factory = new E2ETestFactory(_connectionString, _email);
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

        _ownerA = await ProvisionAndLoginOwnerAsync("attn-corr-a", "Attendance Corr A Co", "owner-a@attn-corr.test");
        var ownerB = await ProvisionAndLoginOwnerAsync("attn-corr-b", "Attendance Corr B Co", "owner-b@attn-corr.test");

        _tenantAId = await GetTenantIdAsync(_ownerA.Host);
        _tenantBId = await GetTenantIdAsync(ownerB.Host);
        _legalEntityAId = await GetPrimaryLegalEntityIdAsync(_ownerA);
        _legalEntityBId = await GetPrimaryLegalEntityIdAsync(ownerB);

        (_requesterA, _requesterAEmployeeId, _requesterAUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, _ownerA.Host, "requester@attn-corr-a.test", _legalEntityAId, "AC-A-REQ-001");
        (_requesterB, _requesterBEmployeeId, _requesterBUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantBId, ownerB.Host, "requester@attn-corr-b.test", _legalEntityBId, "AC-B-REQ-001");
        (_, _, _reviewerAUserId) = await SeedEmployeeFixtureUserAsync(
            _tenantAId, _ownerA.Host, "reviewer@attn-corr-a.test", _legalEntityAId, "AC-A-REV-001");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    // ── Items 1-6: persistence of the approval-snapshot invariant ───────────

    [Fact]
    public async Task ApprovalRequiredRequest_PersistsApprovalRequiredTrue()
    {
        var id = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        var reloaded = await ReloadCorrectionAsync(id);

        reloaded!.ApprovalRequired.Should().BeTrue();
        reloaded.Status.Should().Be(AttendanceCorrection.StatusPending);
    }

    [Fact]
    public async Task AutoApprovedRequest_PersistsApprovalRequiredFalse()
    {
        var id = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusApproved, approvalRequired: false, reviewedById: _requesterAUserId);

        var reloaded = await ReloadCorrectionAsync(id);

        reloaded!.ApprovalRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Approval_PreservesApprovalRequiredTrue()
    {
        var id = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        await UpdateStatusAsync(id, AttendanceCorrection.StatusApproved, _reviewerAUserId);
        var reloaded = await ReloadCorrectionAsync(id);

        reloaded!.Status.Should().Be(AttendanceCorrection.StatusApproved);
        reloaded.ApprovalRequired.Should().BeTrue("approval must not erase the creation-time policy snapshot");
    }

    [Fact]
    public async Task Rejection_PreservesApprovalRequiredTrue()
    {
        var id = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        await UpdateStatusAsync(id, AttendanceCorrection.StatusRejected, _reviewerAUserId);
        var reloaded = await ReloadCorrectionAsync(id);

        reloaded!.Status.Should().Be(AttendanceCorrection.StatusRejected);
        reloaded.ApprovalRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_PreservesApprovalRequiredTrue()
    {
        var id = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        await UpdateStatusAsync(id, AttendanceCorrection.StatusCancelled, _requesterAUserId);
        var reloaded = await ReloadCorrectionAsync(id);

        reloaded!.Status.Should().Be(AttendanceCorrection.StatusCancelled);
        reloaded.ApprovalRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Reload_ThroughFreshDbContext_PreservesBothValues()
    {
        var trueId = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);
        var falseId = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId, _requesterAUserId,
            AttendanceCorrection.StatusApproved, approvalRequired: false, reviewedById: _requesterAUserId);

        // A brand-new scope/DbContext instance per read - not the same tracked instance used to seed -
        // is the point of this test: it proves the column round-trips through Npgsql, not the
        // first-level change-tracker cache.
        (await ReloadCorrectionAsync(trueId))!.ApprovalRequired.Should().BeTrue();
        (await ReloadCorrectionAsync(falseId))!.ApprovalRequired.Should().BeFalse();
    }

    // ── Item 7: the API response layer must read the stored value, not derive it ──

    [Fact]
    public async Task ApiResponse_UsesStoredApprovalRequiredValue_NotDerivedFromStatus()
    {
        // Both rows share the same Status ("approved"); only the persisted ApprovalRequired
        // column differs. If the mapper ever regressed to deriving the field from status, both
        // would report the same (wrong) value here.
        var manuallyApprovedId = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId,
            _requesterAUserId, AttendanceCorrection.StatusApproved, approvalRequired: true, reviewedById: _reviewerAUserId);
        var autoApprovedId = await SeedCorrectionAsync(_tenantAId, _requesterAEmployeeId, _legalEntityAId,
            _requesterAUserId, AttendanceCorrection.StatusApproved, approvalRequired: false, reviewedById: _requesterAUserId);

        var response = await SendAsync(HttpMethod.Get, _requesterA.Host, "/api/v1/attendance/corrections/my",
            body: null, cookie: _requesterA.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadJsonAsync(response);
        var items = page.GetProperty("items");

        page.GetProperty("totalCount").GetInt32().Should().Be(2);

        var manual = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == manuallyApprovedId);
        var auto = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == autoApprovedId);

        manual.GetProperty("approvalRequired").GetBoolean().Should().BeTrue();
        auto.GetProperty("approvalRequired").GetBoolean().Should().BeFalse();
    }

    // ── Items 9-10: RLS actually enforces tenant isolation for this table ───

    [Fact]
    public async Task CrossTenant_CannotReadOtherTenantsCorrection()
    {
        var tenantBCorrectionId = await SeedCorrectionAsync(_tenantBId, _requesterBEmployeeId,
            _legalEntityBId, _requesterBUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        await using var tenantAScopedDb = CreateAppRoleScopedContext(_tenantAId);
        var visible = await tenantAScopedDb.AttendanceCorrections
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == tenantBCorrectionId);

        visible.Should().BeNull("RLS must hide another tenant's row even when the id is known");
    }

    [Fact]
    public async Task CrossTenant_CannotUpdateOtherTenantsCorrection()
    {
        var tenantBCorrectionId = await SeedCorrectionAsync(_tenantBId, _requesterBEmployeeId,
            _legalEntityBId, _requesterBUserId,
            AttendanceCorrection.StatusPending, approvalRequired: true);

        await using (var tenantAScopedDb = CreateAppRoleScopedContext(_tenantAId))
        {
            var affected = await tenantAScopedDb.AttendanceCorrections
                .Where(x => x.Id == tenantBCorrectionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Notes, "cross-tenant-write-attempt"));

            affected.Should().Be(0, "RLS must silently filter the row out of the UPDATE's WHERE clause");
        }

        var unchanged = await ReloadCorrectionAsync(tenantBCorrectionId);
        unchanged!.Notes.Should().BeNull();
    }

    // ── Item 11: the corrective index migration actually created the indexes ──

    [Fact]
    public async Task FreshlyMigratedDatabase_HasAllFiveCorrectiveIndexes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var indexNames = await db.Database
            .SqlQuery<string>(
                $"SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'attendance_corrections'")
            .ToListAsync();

        indexNames.Should().Contain(new[]
        {
            "ix_attendance_corrections_employee_id",
            "ix_attendance_corrections_legal_entity_id",
            "ix_attendance_corrections_presence_session_id",
            "ix_attendance_corrections_requested_by_id",
            "ix_attendance_corrections_reviewed_by_id"
        });
    }

    // ── Seeding and reload helpers ───────────────────────────────────────────

    private async Task<Guid> SeedCorrectionAsync(
        Guid tenantId, Guid employeeId, Guid legalEntityId, Guid requestedById,
        string status, bool approvalRequired, Guid? reviewedById = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var correction = new AttendanceCorrection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            LegalEntityId = legalEntityId,
            WorkDate = DateOnly.FromDateTime(now.UtcDateTime),
            CorrectionType = AttendanceCorrection.TypeClockIn,
            RequestedClockInAt = now,
            Reason = "Integration test fixture row",
            Status = status,
            ApprovalRequired = approvalRequired,
            RequestedById = requestedById,
            ReviewedById = reviewedById,
            ReviewedAt = reviewedById is null ? null : now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Add(correction);
        await db.SaveChangesAsync();
        return correction.Id;
    }

    private async Task UpdateStatusAsync(Guid id, string status, Guid reviewedById)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var correction = await db.AttendanceCorrections.SingleAsync(x => x.Id == id);
        correction.Status = status;
        correction.ReviewedById = reviewedById;
        correction.ReviewedAt = DateTimeOffset.UtcNow;
        correction.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<AttendanceCorrection?> ReloadCorrectionAsync(Guid id)
    {
        // A fresh scope each call - never the scope/context used to seed or mutate the row -
        // is what makes this a real "survives a fresh DbContext" check rather than a
        // first-level-cache echo.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AttendanceCorrections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    }

    /// <summary>
    /// Opens an independent connection as the restricted onevo_app role, with the same
    /// TenantRlsInterceptor production uses, scoped to the given tenant - unlike every other
    /// DbContext in this file (which comes from the WebApplicationFactory's DI container and
    /// therefore connects as the Testcontainers superuser, bypassing RLS entirely; see
    /// E2ETestFactory.ConfigureWebHost), this is the one context in this test class where RLS is
    /// actually enforced, which is the point of the two CrossTenant_* tests above.
    /// </summary>
    private ApplicationDbContext CreateAppRoleScopedContext(Guid tenantId)
    {
        var appRoleConnectionString = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Username = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword
        }.ConnectionString;

        var dateTimeProvider = new SystemDateTimeProvider();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(appRoleConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(new FixedTenantContext(tenantId)))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new FixedTenantContext(tenantId));
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public string? Slug => null;
        public TenantStatus? Status => null;
        public bool IsResolved => true;
        public TenantContextMode ContextMode => TenantContextMode.Tenant;
    }

    // ── Provisioning and fixture-seeding boilerplate (mirrors LegalEntitiesIntegrationTests /
    // LeaveTypesIntegrationTests verbatim; not duplicated business-domain test coverage) ──

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

    private async Task<(TenantSession Session, Guid EmployeeId, Guid UserId)> SeedEmployeeFixtureUserAsync(
        Guid tenantId, string host, string email, Guid legalEntityId, string employeeNumber)
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
            LastName = "Employee",
            PasswordHash = hasher.Hash(FixtureUserPassword),
            IsActive = true,
            EmailVerified = true,
            MustChangePassword = false,
            PasswordSetByAdmin = false,
            CreatedAt = now,
            CreatedById = userId
        });

        var employeeId = Guid.NewGuid();
        db.Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = userId,
            EmployeeNumber = employeeNumber,
            FirstName = "Fixture",
            LastName = "Employee",
            Email = email,
            LegalEntityId = legalEntityId,
            EmploymentTypeId = 1,
            EmploymentStatusId = 1,
            WorkModeId = 1,
            HireDate = new DateOnly(2025, 1, 1),
            CreatedAt = now,
            CreatedById = userId
        });

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

        var session = await LoginViaBaseHostAsync(host, email, FixtureUserPassword);
        return (session, employeeId, userId);
    }

    private async Task<Guid> GetTenantIdAsync(string host)
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

    private async Task<JsonElement> GetJsonAsync(TenantSession session, string path)
    {
        var response = await SendAsync(HttpMethod.Get, session.Host, path, body: null, cookie: session.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(response);
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
