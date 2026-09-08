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
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Settings;

/// <summary>
/// DB-backed coverage for the monitoring policy override CRUD surface
/// (MonitoringPolicyConfigurationService + MonitoringSettingsController's
/// company/department/position/role routes) and, most importantly, a regression
/// test proving MonitoringToggleResolverService resolves a position-level override
/// via a real PositionAssignments row rather than the historically-hardcoded-null
/// positionId. Mirrors the fixture/assertion style of
/// MonitoringFeatureTogglesIntegrationTests and TrayMonitoringPolicyIntegrationTests.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MonitoringPolicyOverrideIntegrationTests : IAsyncLifetime
{
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_monitoring_override_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly SystemDateTimeProvider _clock = new();

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

    // ---------------------------------------------------------------------
    // Highest priority: regression test for the historically-hardcoded-null
    // positionId bug. Proves the resolver reaches an *active* position override
    // through a real PositionAssignments row (PrimaryEmployment/Active/in-range).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Resolve_PositionOverride_UsesRealPositionAssignmentRow()
    {
        var tenantId = Guid.NewGuid();
        var (employee, positionId) = await SeedTenantAndEmployeeAsync(
            tenantId, "pos-regress", departmentId: null, assignToPosition: true);

        await SeedCompanyToggleAsync(tenantId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "position", positionId, activityMonitoring: true);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(tenantId, employee.UserId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue("the position override should apply via the employee's real active PositionAssignments row");
    }

    // ---------------------------------------------------------------------
    // Precedence matrix: employee > role > position > department > company.
    // Employee-level is covered by MonitoringFeatureTogglesIntegrationTests already
    // exercising the tenant-toggle fallback path; these four cover the remaining chain.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Resolve_CompanyDefaultOnly_Resolves()
    {
        var tenantId = Guid.NewGuid();
        var (employee, _) = await SeedTenantAndEmployeeAsync(
            tenantId, "prec-company", departmentId: null, assignToPosition: false);
        await SeedCompanyToggleAsync(tenantId, activityMonitoring: true);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(tenantId, employee.UserId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_DepartmentOverride_WinsOverCompanyDefault()
    {
        var tenantId = Guid.NewGuid();
        var (employee, departmentId) = await SeedTenantAndEmployeeWithDepartmentAsync(tenantId, "prec-dept");

        await SeedCompanyToggleAsync(tenantId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "department", departmentId, activityMonitoring: true);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(tenantId, employee.UserId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_PositionOverride_WinsOverDepartmentOverride()
    {
        var tenantId = Guid.NewGuid();
        var (employee, departmentId, positionId) = await SeedTenantEmployeeDeptAndPositionAsync(tenantId, "prec-pos");

        await SeedCompanyToggleAsync(tenantId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "department", departmentId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "position", positionId, activityMonitoring: true);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(tenantId, employee.UserId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue("position-level override must win over the conflicting department-level override");
    }

    [Fact]
    public async Task Resolve_RoleOverride_WinsOverPositionOverride()
    {
        var tenantId = Guid.NewGuid();
        var (employee, _, positionId) = await SeedTenantEmployeeDeptAndPositionAsync(tenantId, "prec-role");
        var roleId = await SeedRoleAssignmentAsync(tenantId, employee.UserId);

        await SeedCompanyToggleAsync(tenantId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "position", positionId, activityMonitoring: false);
        await SeedOverrideAsync(tenantId, "role", roleId, activityMonitoring: true);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var enabled = await resolver.IsEnabledAsync(tenantId, employee.UserId, MonitoringCapability.ActivityMonitoring);

        enabled.Should().BeTrue("role-level override must win over the conflicting position-level override");
    }

    // ---------------------------------------------------------------------
    // Missing policy fails closed: no MonitoringFeatureToggle row, no overrides at all.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Resolve_NoTogglesNoOverrides_AllCapabilitiesFalseAndDefaultThreshold()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantOnlyAsync(tenantId, "prec-empty");

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var employeeId = Guid.NewGuid();

        foreach (var capability in Enum.GetValues<MonitoringCapability>())
        {
            var enabled = await resolver.IsEnabledAsync(tenantId, employeeId, capability);
            enabled.Should().BeFalse($"{capability} must fail closed when no toggle/override row exists");
        }

        var minutes = await resolver.GetIdleThresholdMinutesAsync(tenantId, employeeId);
        minutes.Should().Be(2, "the resolver's hardcoded safe default (MonitoringToggleResolution.DefaultIdleThresholdMinutes)");
    }

    // ---------------------------------------------------------------------
    // Invalid / cross-tenant override targets fail cleanly (NotFound), not with an exception.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Upsert_NonExistentTargetId_ReturnsNotFound()
    {
        var session = await SeedAdminSessionAsync("upsert-missing");

        var resp = await PutOverrideAsync(session, "department", Guid.NewGuid());

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upsert_CrossTenantTarget_ReturnsNotFound()
    {
        var session = await SeedAdminSessionAsync("upsert-crosstenant-a");
        var otherTenantId = Guid.NewGuid();
        var otherDepartmentId = await SeedDepartmentInForeignTenantAsync(otherTenantId, "upsert-crosstenant-b");

        // Sanity check: prove the row genuinely exists under tenant B before asserting tenant A's
        // PUT rejects it - otherwise a silently-dropped/never-persisted seed would make this test
        // pass vacuously (any nonexistent id 404s) without proving cross-tenant isolation at all.
        await using (var verifyDb = CreateTenantScopedContext(otherTenantId, "upsert-crosstenant-b"))
        {
            var exists = await verifyDb.Departments.AnyAsync(d => d.Id == otherDepartmentId && d.TenantId == otherTenantId);
            exists.Should().BeTrue("the foreign-tenant department must actually be persisted for this test to prove anything");
        }

        var resp = await PutOverrideAsync(session, "department", otherDepartmentId);

        resp.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "a department belonging to a different tenant must not validate as a valid override target");
    }

    [Fact]
    public async Task Delete_NonExistentOverride_ReturnsNotFound()
    {
        var session = await SeedAdminSessionAsync("delete-missing");

        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/v1/attendance/monitoring/policy/department/{Guid.NewGuid()}");
        req.Headers.Host = session.TenantHost;
        req.Headers.Add("Cookie", session.CookieHeader);
        req.Headers.Add("X-CSRF-Token", session.CsrfHeader);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

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

    /// <summary>
    /// Seeds a Tenant/LegalEntity/Department for a tenant the current test's session has no
    /// membership in. The Tenant row itself is created via the plain DI-resolved DbContext (not
    /// yet tenant-scoped - there is no tenant to scope to until this call creates one), but
    /// LegalEntity/Department are tenant-owned rows subject to the tenant_isolation RLS policy,
    /// so they are written through a DbContext whose TenantRlsInterceptor is explicitly resolved
    /// to this foreign tenant (mirrors TryCreateActiveAssignmentTests.CreateContext) - otherwise
    /// the insert would either fail the RLS WITH CHECK clause or silently apply against the
    /// wrong tenant context.
    /// </summary>
    private async Task<Guid> SeedDepartmentInForeignTenantAsync(Guid tenantId, string slug)
    {
        using (var scope = _factory.Services.CreateScope())
        {
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
        }

        await using var tenantDb = CreateTenantScopedContext(tenantId, slug);
        var legalEntityId = Guid.NewGuid();
        tenantDb.LegalEntities.Add(new LegalEntity
        {
            Id = legalEntityId,
            TenantId = tenantId,
            Name = $"{slug}-le",
            CountryCode = "US",
            CurrencyCode = "USD"
        });
        var departmentId = Guid.NewGuid();
        tenantDb.Departments.Add(new Department
        {
            Id = departmentId,
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = $"{slug}-dept",
            IsActive = true
        });
        await tenantDb.SaveChangesAsync();
        return departmentId;
    }

    private ApplicationDbContext CreateTenantScopedContext(Guid tenantId, string slug)
    {
        var tenantContext = new TenantContextAccessor();
        tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
            tenantId, slug, TenantStatus.Active, null));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
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

    private async Task<Guid> SeedRoleAssignmentAsync(Guid tenantId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = "resolver-role",
            Description = "Monitoring precedence fixture role",
            IsSystem = false,
            CreatedAt = now,
            CreatedById = userId
        });
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = now,
            AssignedBy = userId
        });
        await db.SaveChangesAsync();
        return roleId;
    }

    private Task<Guid> SeedTenantOnlyAsync(Guid tenantId, string slug) => SeedTenantAsync(tenantId, slug);

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

    private async Task<(Employee Employee, Guid PositionId)> SeedTenantAndEmployeeAsync(
        Guid tenantId, string slug, Guid? departmentId, bool assignToPosition)
    {
        await SeedTenantAsync(tenantId, slug);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position
        {
            Id = positionId,
            TenantId = tenantId,
            Name = $"{slug}-position",
            PositionType = Position.TypeUnique,
            MaxOccupancy = 1,
            IsActive = true
        });

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@{slug}.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DepartmentId = departmentId
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        if (assignToPosition)
        {
            db.PositionAssignments.Add(new PositionAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                PositionId = positionId,
                AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
                AssignmentStatus = PositionAssignmentStatus.Active,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                EffectiveTo = null
            });
            await db.SaveChangesAsync();
        }

        return (employee, positionId);
    }

    private async Task<(Employee Employee, Guid DepartmentId)> SeedTenantAndEmployeeWithDepartmentAsync(
        Guid tenantId, string slug)
    {
        await SeedTenantAsync(tenantId, slug);

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
        var departmentId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = departmentId,
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = $"{slug}-dept",
            IsActive = true
        });

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Test",
            LastName = "Employee",
            Email = $"{Guid.NewGuid():N}@{slug}.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DepartmentId = departmentId
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        return (employee, departmentId);
    }

    private async Task<(Employee Employee, Guid DepartmentId, Guid PositionId)> SeedTenantEmployeeDeptAndPositionAsync(
        Guid tenantId, string slug)
    {
        var (employee, departmentId) = await SeedTenantAndEmployeeWithDepartmentAsync(tenantId, slug);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var positionId = Guid.NewGuid();
        db.Positions.Add(new Position
        {
            Id = positionId,
            TenantId = tenantId,
            DepartmentId = departmentId,
            Name = $"{slug}-position",
            PositionType = Position.TypeUnique,
            MaxOccupancy = 1,
            IsActive = true
        });
        db.PositionAssignments.Add(new PositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EffectiveTo = null
        });
        await db.SaveChangesAsync();

        return (employee, departmentId, positionId);
    }

    private Task<SessionInfo> SeedAdminSessionAsync(string slug) =>
        SeedUserWithPermissionsAsync(slug, ["monitoring:read", "monitoring:configure"]);

    private async Task<SessionInfo> SeedUserWithPermissionsAsync(string slug, IReadOnlyList<string> permissionCodes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
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
        db.Tenants.Add(tenant);
        db.Users.Add(user);

        var now = DateTimeOffset.UtcNow;
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
            SelectedModulesJson = """["activity_monitoring"]""",
            CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
            CurrentPeriodEnd = DateOnly.FromDateTime(now.UtcDateTime.AddMonths(1)),
            ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAt = now
        });

        var roleId = Guid.NewGuid();
        db.Add(new Role
        {
            Id = roleId,
            TenantId = tenant.Id,
            Name = $"{slug}-role",
            Description = "Monitoring override fixture role",
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

        await db.SaveChangesAsync();

        var sessionInfo = await LoginAndGetSessionAsync(userId, $"{slug}@test.dev", "TestPass1!", slug);
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
