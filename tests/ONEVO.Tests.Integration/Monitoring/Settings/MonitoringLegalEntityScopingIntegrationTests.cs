using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Settings;

/// <summary>
/// Legal-entity (company) scoping coverage for the monitoring policy admin surface
/// (MonitoringSettingsController + MonitoringPolicyConfigurationService) and for the
/// resolver's employee lookup used by the tray effective-policy endpoint. Complements
/// MonitoringPolicyOverrideIntegrationTests, which already covers the pre-existing
/// cross-tenant department-target rejection and the precedence chain.
///
/// legalEntityId is never accepted from a request body/route anywhere in this feature -
/// it is always ICurrentUser.LegalEntityId, itself derived server-side in
/// TenantDatabaseTicketStore.RetrieveAsync from Session.ActiveEmployeeId ->
/// Employee.LegalEntityId. That is verified structurally (there is no such parameter to
/// pass) rather than by a request that tries to smuggle one in and gets ignored.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MonitoringLegalEntityScopingIntegrationTests : IAsyncLifetime
{
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_monitoring_legalentity_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await IntegrationDatabaseBootstrap.InitializeAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, new CapturingEmailService());
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task LegalEntityDefaults_PersistIndependently_AndRejectDuplicates()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-defaults");
        var legalEntityA = await SeedLegalEntityAsync(tenantId, "le-defaults-a");
        var legalEntityB = await SeedLegalEntityAsync(tenantId, "le-defaults-b");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MonitoringFeatureToggles.AddRange(
            Toggle(tenantId, legalEntityA, activityMonitoring: true),
            Toggle(tenantId, legalEntityB, activityMonitoring: false));
        await db.SaveChangesAsync();

        (await db.MonitoringFeatureToggles.SingleAsync(x => x.LegalEntityId == legalEntityA))
            .ActivityMonitoring.Should().BeTrue();
        (await db.MonitoringFeatureToggles.SingleAsync(x => x.LegalEntityId == legalEntityB))
            .ActivityMonitoring.Should().BeFalse();

        db.MonitoringFeatureToggles.Add(Toggle(tenantId, legalEntityA, activityMonitoring: false));
        var saveDuplicate = () => db.SaveChangesAsync();
        await saveDuplicate.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------------------------------------------------------------------
    // A department override that belongs to a different legal entity (company) than the
    // caller's own current one must not be visible, editable, or deletable - even though
    // both legal entities belong to the SAME tenant, so the pre-existing cross-tenant
    // TargetExistsAsync(TenantId) check alone would not catch this.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Get_DepartmentOverrideInAnotherLegalEntity_IsNotVisible()
    {
        var fixture = await SeedTwoLegalEntityTenantAsync("le-get");
        await SeedOverrideAsync(fixture.TenantId, "department", fixture.DepartmentInLegalEntityB, activityMonitoring: true);

        var respA = await GetPolicyAsync(fixture.SessionInLegalEntityA);
        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA = await respA.Content.ReadFromJsonAsync<JsonElement>();
        bodyA.GetProperty("overrides").GetArrayLength().Should().Be(
            0, "the override belongs to legal entity B's department, not A's active company");
        bodyA.GetProperty("hasActiveCompanyContext").GetBoolean().Should().BeTrue();

        var respB = await GetPolicyAsync(fixture.SessionInLegalEntityB);
        var bodyB = await respB.Content.ReadFromJsonAsync<JsonElement>();
        bodyB.GetProperty("overrides").GetArrayLength().Should().Be(
            1, "the same override must be visible to a session whose active company is legal entity B");
    }

    [Fact]
    public async Task Upsert_DepartmentTargetInAnotherLegalEntity_ReturnsNotFound()
    {
        var fixture = await SeedTwoLegalEntityTenantAsync("le-upsert");

        var resp = await PutOverrideAsync(fixture.SessionInLegalEntityA, "department", fixture.DepartmentInLegalEntityB);

        resp.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "a department belonging to a different legal entity than the caller's active company must not validate as a valid target");
    }

    [Fact]
    public async Task Delete_DepartmentOverrideInAnotherLegalEntity_ReturnsNotFoundAndDoesNotDelete()
    {
        var fixture = await SeedTwoLegalEntityTenantAsync("le-delete");
        await SeedOverrideAsync(fixture.TenantId, "department", fixture.DepartmentInLegalEntityB, activityMonitoring: true);

        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/v1/attendance/monitoring/policy/department/{fixture.DepartmentInLegalEntityB}");
        req.Headers.Host = fixture.SessionInLegalEntityA.TenantHost;
        req.Headers.Add("Cookie", fixture.SessionInLegalEntityA.CookieHeader);
        req.Headers.Add("X-CSRF-Token", fixture.SessionInLegalEntityA.CsrfHeader);
        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stillExists = await db.MonitoringPolicyOverrides.AnyAsync(o =>
            o.TenantId == fixture.TenantId && o.ScopeType == "department" && o.ScopeId == fixture.DepartmentInLegalEntityB);
        stillExists.Should().BeTrue("a cross-company delete attempt must not remove the target company's override");
    }

    // ---------------------------------------------------------------------
    // A user with no active employee/company context (e.g. a tenant admin with no Employee
    // row yet) must not see or edit any department/position override - fail closed - but the
    // tenant-wide company default (Phase 1 schema: monitoring_feature_toggles is one row per
    // tenant, not per legal entity) and role overrides (tenant-wide, not legal-entity-scoped)
    // are still returned rather than the page being blanked out entirely.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Get_NoActiveCompanyContext_HidesDepartmentAndPositionOverridesButKeepsCompanyDefaultAndRoleOverrides()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-nocontext");
        await SeedCompanyToggleAsync(tenantId, activityMonitoring: true);

        var legalEntityId = await SeedLegalEntityAsync(tenantId, "le-nocontext");
        var departmentId = await SeedDepartmentAsync(tenantId, legalEntityId, "le-nocontext-dept");
        await SeedOverrideAsync(tenantId, "department", departmentId, activityMonitoring: false);

        var session = await SeedUserWithPermissionsAsync(tenantId, "le-nocontext-admin", ["monitoring:read", "monitoring:configure"]);
        var roleId = await SeedRoleOverrideForSessionUserAsync(tenantId, session, activityMonitoring: false);

        var resp = await GetPolicyAsync(session);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("hasActiveCompanyContext").GetBoolean().Should().BeFalse();
        body.GetProperty("companyDefault").GetProperty("activityMonitoring").GetBoolean().Should().BeTrue(
            "the tenant-wide company default must still be returned even with no active company context");

        var overrides = body.GetProperty("overrides").EnumerateArray().ToList();
        overrides.Should().ContainSingle(o => o.GetProperty("scopeType").GetString() == "role" && o.GetProperty("scopeId").GetGuid() == roleId,
            "role overrides are tenant-wide and unaffected by legal-entity context");
        overrides.Should().NotContain(o => o.GetProperty("scopeType").GetString() == "department",
            "department overrides require an active company context to attribute them to");
    }

    [Fact]
    public async Task Upsert_DepartmentOverride_NoActiveCompanyContext_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-nocontext-upsert");
        var legalEntityId = await SeedLegalEntityAsync(tenantId, "le-nocontext-upsert");
        var departmentId = await SeedDepartmentAsync(tenantId, legalEntityId, "le-nocontext-upsert-dept");
        var session = await SeedUserWithPermissionsAsync(tenantId, "le-nocontext-upsert-admin", ["monitoring:read", "monitoring:configure"]);

        var resp = await PutOverrideAsync(session, "department", departmentId);

        resp.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "without an active company context there is no legal entity to validate the department target against");
    }

    // ---------------------------------------------------------------------
    // Permission gating: a user without monitoring:read/monitoring:configure cannot reach
    // the endpoints at all, independent of legal-entity scoping.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Get_UserWithoutMonitoringReadPermission_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-noperm");
        var session = await SeedUserWithPermissionsAsync(tenantId, "le-noperm-user", []);

        var resp = await GetPolicyAsync(session);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_UserWithReadOnlyPermission_CannotConfigureCompanyDefault()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-readonly");
        var session = await SeedUserWithPermissionsAsync(tenantId, "le-readonly-user", ["monitoring:read"]);

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/v1/attendance/monitoring/policy/company");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        req.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        req.Content = JsonContent.Create(new
        {
            activityMonitoring = true,
            applicationTracking = (bool?)null,
            documentTracking = (bool?)null,
            communicationTracking = (bool?)null,
            screenshotCapture = (bool?)null,
            autoScreenshotCapture = (bool?)null,
            meetingDetection = (bool?)null,
            deviceTracking = (bool?)null,
            workLocationVerification = (bool?)null,
            identityVerification = (bool?)null,
            biometric = (bool?)null,
            idleThresholdMinutes = (int?)null
        });
        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "monitoring:read alone must not grant monitoring:configure's write access");
    }

    // ---------------------------------------------------------------------
    // Resolver determinism for multi-company users: MonitoringToggleResolverService must
    // resolve the same, deterministic Employee row for a UserId that owns more than one
    // Employee row (one per company), not an arbitrary one - this backs the tray
    // effective-policy endpoint, which identifies the caller only by UserId (device JWT),
    // never by a specific EmployeeId.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Resolve_MultiCompanyUser_FailsClosedWithoutContext_AndUsesRequestedLegalEntity()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, "le-multicompany");
        await SeedCompanyToggleAsync(tenantId, activityMonitoring: false);

        var legalEntityA = await SeedLegalEntityAsync(tenantId, "le-multicompany-a");
        var legalEntityB = await SeedLegalEntityAsync(tenantId, "le-multicompany-b");
        var departmentA = await SeedDepartmentAsync(tenantId, legalEntityA, "le-multicompany-a-dept");
        var departmentB = await SeedDepartmentAsync(tenantId, legalEntityB, "le-multicompany-b-dept");

        // Department A resolves true, department B resolves false - opposite values so a wrong
        // pick is observable rather than accidentally matching either way.
        await SeedOverrideAsync(tenantId, "department", departmentA, activityMonitoring: true);
        await SeedOverrideAsync(tenantId, "department", departmentB, activityMonitoring: false);

        var userId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Older, no-longer-active assignment in company B: seeded first so a naive
            // insertion-order lookup would pick this one.
            var employeeB = new Employee
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
                EmployeeNumber = Guid.NewGuid().ToString("N")[..8], FirstName = "Multi", LastName = "CompanyB",
                Email = $"{Guid.NewGuid():N}@le-multicompany.onevo.dev",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow), DepartmentId = departmentB, LegalEntityId = legalEntityB
            };
            db.Employees.Add(employeeB);

            var employeeA = new Employee
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
                EmployeeNumber = Guid.NewGuid().ToString("N")[..8], FirstName = "Multi", LastName = "CompanyA",
                Email = $"{Guid.NewGuid():N}@le-multicompany.onevo.dev",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow), DepartmentId = departmentA, LegalEntityId = legalEntityA
            };
            db.Employees.Add(employeeA);

            var positionB = new Position
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityB,
                Name = "le-multicompany-b-position", PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true
            };
            var positionA = new Position
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityA,
                Name = "le-multicompany-a-position", PositionType = Position.TypeUnique, MaxOccupancy = 1, IsActive = true
            };
            db.Positions.Add(positionB);
            db.Positions.Add(positionA);
            await db.SaveChangesAsync();

            db.PositionAssignments.Add(new PositionAssignment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeB.Id, PositionId = positionB.Id,
                AssignmentKind = PositionAssignmentKind.PrimaryEmployment, AssignmentStatus = PositionAssignmentStatus.Active,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), EffectiveTo = null
            });
            db.PositionAssignments.Add(new PositionAssignment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeA.Id, PositionId = positionA.Id,
                AssignmentKind = PositionAssignmentKind.PrimaryEmployment, AssignmentStatus = PositionAssignmentStatus.Active,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), EffectiveTo = null
            });
            await db.SaveChangesAsync();
        }

        using var resolverScope = _factory.Services.CreateScope();
        var resolver = resolverScope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var withoutContext = await resolver.IsEnabledAsync(
            tenantId, userId, MonitoringCapability.ActivityMonitoring);
        var forCompanyA = await resolver.IsEnabledAsync(
            tenantId, userId, legalEntityA, MonitoringCapability.ActivityMonitoring);
        var forCompanyB = await resolver.IsEnabledAsync(
            tenantId, userId, legalEntityB, MonitoringCapability.ActivityMonitoring);

        withoutContext.Should().BeFalse(
            "a multi-company user without trusted legal-entity context must fail closed");
        forCompanyA.Should().BeTrue("company A's employee belongs to department A");
        forCompanyB.Should().BeFalse("company B must not inherit company A's department policy");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<HttpResponseMessage> GetPolicyAsync(SessionInfo session)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/attendance/monitoring/policy");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        return await _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PutOverrideAsync(SessionInfo session, string scopeType, Guid targetId)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/attendance/monitoring/policy/{scopeType}/{targetId}");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        req.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        req.Content = JsonContent.Create(new
        {
            activityMonitoring = true,
            applicationTracking = (bool?)null,
            documentTracking = (bool?)null,
            communicationTracking = (bool?)null,
            screenshotCapture = (bool?)null,
            autoScreenshotCapture = (bool?)null,
            meetingDetection = (bool?)null,
            deviceTracking = (bool?)null,
            workLocationVerification = (bool?)null,
            identityVerification = (bool?)null,
            biometric = (bool?)null,
            idleThresholdMinutes = 10,
            overrideReason = "test"
        });

        return await _client.SendAsync(req);
    }

    private sealed record TwoLegalEntityFixture(
        Guid TenantId, Guid DepartmentInLegalEntityA, Guid DepartmentInLegalEntityB,
        SessionInfo SessionInLegalEntityA, SessionInfo SessionInLegalEntityB);

    /// <summary>Seeds one tenant with two legal entities/departments and one admin user per
    /// legal entity (each with a single Employee row, so login deterministically activates
    /// that company - no ambiguity, no reliance on SwitchActiveCompany).</summary>
    private async Task<TwoLegalEntityFixture> SeedTwoLegalEntityTenantAsync(string slug)
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId, slug);

        var legalEntityA = await SeedLegalEntityAsync(tenantId, $"{slug}-a");
        var legalEntityB = await SeedLegalEntityAsync(tenantId, $"{slug}-b");
        var departmentA = await SeedDepartmentAsync(tenantId, legalEntityA, $"{slug}-a-dept");
        var departmentB = await SeedDepartmentAsync(tenantId, legalEntityB, $"{slug}-b-dept");

        var sessionA = await SeedUserWithPermissionsAsync(
            tenantId, $"{slug}-admin-a", ["monitoring:read", "monitoring:configure"], legalEntityA);
        var sessionB = await SeedUserWithPermissionsAsync(
            tenantId, $"{slug}-admin-b", ["monitoring:read", "monitoring:configure"], legalEntityB);

        return new TwoLegalEntityFixture(tenantId, departmentA, departmentB, sessionA, sessionB);
    }

    private async Task<Guid> SeedLegalEntityAsync(Guid tenantId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legalEntityId = Guid.NewGuid();
        db.LegalEntities.Add(new LegalEntity
        {
            Id = legalEntityId,
            TenantId = tenantId,
            Name = $"{slug}-le",
            CountryCode = "US",
            CurrencyCode = "USD"
        });
        await db.SaveChangesAsync();
        return legalEntityId;
    }

    private async Task<Guid> SeedDepartmentAsync(Guid tenantId, Guid legalEntityId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var departmentId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = departmentId,
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = slug,
            IsActive = true
        });
        await db.SaveChangesAsync();
        return departmentId;
    }

    private async Task SeedCompanyToggleAsync(Guid tenantId, bool activityMonitoring)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MonitoringFeatureToggles.Add(new MonitoringFeatureToggles
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActivityMonitoring = activityMonitoring,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static MonitoringFeatureToggles Toggle(
        Guid tenantId, Guid legalEntityId, bool activityMonitoring) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LegalEntityId = legalEntityId,
        ActivityMonitoring = activityMonitoring,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private async Task SeedOverrideAsync(Guid tenantId, string scopeType, Guid scopeId, bool activityMonitoring)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MonitoringPolicyOverrides.Add(new MonitoringPolicyOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            ActivityMonitoring = activityMonitoring,
            SetById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRoleOverrideForSessionUserAsync(Guid tenantId, SessionInfo session, bool activityMonitoring)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleId = Guid.NewGuid();
        db.MonitoringPolicyOverrides.Add(new MonitoringPolicyOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScopeType = "role",
            ScopeId = roleId,
            ActivityMonitoring = activityMonitoring,
            SetById = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return roleId;
    }

    private async Task<Guid> SeedTenantAsync(Guid tenantId, string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    /// <summary>Seeds a user (+ role/permissions) and, when <paramref name="legalEntityId"/> is
    /// supplied, a single Employee row in that legal entity so the resulting session's
    /// TenantDatabaseTicketStore-derived active company is deterministic (the user's only
    /// Employee row). Omitting it models a user with no Employee row at all (no active company
    /// context).</summary>
    private Task<SessionInfo> SeedUserWithPermissionsAsync(
        Guid tenantId, string slug, IReadOnlyList<string> permissionCodes, Guid? legalEntityId = null) =>
        SeedUserWithPermissionsAsync(tenantId, slug, permissionCodes, legalEntityId, departmentId: null);

    private async Task<SessionInfo> SeedUserWithPermissionsAsync(
        Guid tenantId, string slug, IReadOnlyList<string> permissionCodes, Guid? legalEntityId, Guid? departmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            TenantId = tenant.Id,
            Email = $"{slug}@test.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass1!", 12),
            FirstName = "Test",
            LastName = "Admin",
            IsActive = true
        };
        db.Users.Add(user);

        var now = DateTimeOffset.UtcNow;
        if (!await db.TenantSubscriptions.AnyAsync(s => s.TenantId == tenant.Id))
        {
            db.Add(new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                PlanId = SeededPlanId,
                Status = "active",
                BillingCycle = "monthly",
                CommercialModel = "subscription",
                BillingCurrency = "USD",
                CompanySizeRange = "1-10",
                SelectedModulesJson = """["monitoring"]""",
                CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
                CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(1)),
                ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime),
                CreatedAt = now
            });
        }

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenant.Id,
            Name = $"{slug}-role",
            Description = "Monitoring legal-entity scoping fixture role",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });
        foreach (var code in permissionCodes)
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == code);
            db.Add(new RolePermission { TenantId = tenant.Id, RoleId = roleId, PermissionId = permission.Id });
        }
        db.Add(new UserRole
        {
            TenantId = tenant.Id, UserId = userId, RoleId = roleId,
            AssignedAt = now, AssignedBy = userId
        });

        if (legalEntityId is Guid activeLegalEntityId)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = userId,
                EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
                FirstName = "Test",
                LastName = "Admin",
                Email = $"{Guid.NewGuid():N}@{slug}.onevo.dev",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
                LegalEntityId = activeLegalEntityId,
                DepartmentId = departmentId
            });
        }

        await db.SaveChangesAsync();

        var sessionInfo = await LoginAndGetSessionAsync(userId, $"{slug}@test.dev", "TestPass1!", tenant.Slug);
        return sessionInfo with { TenantId = tenant.Id };
    }

    private async Task<SessionInfo> LoginAndGetSessionAsync(Guid userId, string email, string password, string tenantSlug)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.Host = "localhost";
        loginRequest.Content = JsonContent.Create(new { email, password });
        var loginResponse = await _client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await loginResponse.Content.ReadAsStringAsync());

        var legalResponse = await CompleteLegalAcceptanceAsync(loginResponse);
        legalResponse.StatusCode.Should().Be(HttpStatusCode.Accepted, await legalResponse.Content.ReadAsStringAsync());

        var exchangeResponse = await CompleteTenantSessionExchangeAsync(legalResponse);
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exchangeResponse.Content.ReadAsStringAsync());

        var sessionValue = ExtractCookieValue(exchangeResponse, "onevo_session");
        var csrfCookieValue = ExtractCookieValue(exchangeResponse, "onevo_csrf");
        var csrfHeader = Uri.UnescapeDataString(csrfCookieValue);

        return new SessionInfo(
            $"onevo_session={sessionValue}; onevo_csrf={csrfCookieValue}",
            csrfHeader,
            $"{tenantSlug}.localhost",
            Guid.Empty);
    }

    private async Task<HttpResponseMessage> CompleteLegalAcceptanceAsync(HttpResponseMessage priorResponse)
    {
        var legalPending = ExtractCookieValue(priorResponse, "onevo_legal_pending");
        var legalCsrf = ExtractCookieValue(priorResponse, "onevo_legal_csrf");
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, continueUrl.PathAndQuery);
        request.Headers.Host = continueUrl.Host;
        request.Headers.Add("Cookie", $"onevo_legal_pending={legalPending}; onevo_legal_csrf={legalCsrf}");
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

    private async Task<HttpResponseMessage> CompleteTenantSessionExchangeAsync(HttpResponseMessage priorResponse)
    {
        var priorBody = await priorResponse.Content.ReadAsStringAsync();
        using var priorDocument = JsonDocument.Parse(priorBody);
        var continueUrl = new Uri(priorDocument.RootElement.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/session-exchange");
        request.Headers.Host = continueUrl.Host;
        request.Content = JsonContent.Create(new { code });
        return await _client.SendAsync(request);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values : Enumerable.Empty<string>();
        foreach (var cookie in setCookies)
        {
            var pair = cookie.Split(';')[0];
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == cookieName)
                return parts[1];
        }
        throw new InvalidOperationException($"Cookie '{cookieName}' not found in response.");
    }

    private sealed record SessionInfo(string CookieHeader, string CsrfHeader, string TenantHost, Guid TenantId);
}
