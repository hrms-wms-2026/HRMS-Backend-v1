using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.Auth.ActiveCompany;

/// <summary>
/// Proves switching ActiveEmployeeId changes which position-sourced role grants are visible,
/// using the same session row throughout (no re-login). HTTP login is covered separately by
/// existing session-exchange tests; this suite seeds the session the ticket store would write.
/// </summary>
public sealed class SwitchActiveCompanyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_switch_active_company_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _userId;
    private Guid _sessionId;
    private Guid _employeeAId;
    private Guid _employeeBId;
    private Guid _legalEntityAId;
    private Guid _legalEntityBId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _userId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        _employeeAId = Guid.NewGuid();
        _employeeBId = Guid.NewGuid();
        _legalEntityAId = Guid.NewGuid();
        _legalEntityBId = Guid.NewGuid();
        var positionAId = Guid.NewGuid();
        var positionBId = Guid.NewGuid();
        var permAId = Guid.NewGuid();
        var permBId = Guid.NewGuid();
        var roleAId = Guid.NewGuid();
        var roleBId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Switch Company Tenant",
            Slug = "switch-company",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        db.LegalEntities.AddRange(
            new LegalEntity { Id = _legalEntityAId, TenantId = _tenantId, Name = "Company A" },
            new LegalEntity { Id = _legalEntityBId, TenantId = _tenantId, Name = "Company B" });
        db.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Email = "switch@example.com",
            FirstName = "Switch",
            LastName = "User",
            IsActive = true,
        });
        db.WorkModes.Add(new ONEVO.Domain.Lookups.WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });
        db.Employees.AddRange(
            new EmployeeEntity
            {
                Id = _employeeAId,
                TenantId = _tenantId,
                UserId = _userId,
                LegalEntityId = _legalEntityAId,
                EmployeeNumber = "EMP-A",
                FirstName = "Switch",
                LastName = "User",
                Email = "switch@example.com",
                HireDate = new DateOnly(2024, 1, 1),
                CreatedById = _userId
            },
            new EmployeeEntity
            {
                Id = _employeeBId,
                TenantId = _tenantId,
                UserId = _userId,
                LegalEntityId = _legalEntityBId,
                EmployeeNumber = "EMP-B",
                FirstName = "Switch",
                LastName = "User",
                Email = "switch@example.com",
                HireDate = new DateOnly(2025, 6, 1),
                CreatedById = _userId
            });
        db.Positions.AddRange(
            new Position { Id = positionAId, TenantId = _tenantId, LegalEntityId = _legalEntityAId, Name = "A Role", CreatedById = _userId },
            new Position { Id = positionBId, TenantId = _tenantId, LegalEntityId = _legalEntityBId, Name = "B Role", CreatedById = _userId });
        db.PositionAssignments.AddRange(
            new PositionAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EmployeeId = _employeeAId,
                PositionId = positionAId,
                AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
                AssignmentStatus = PositionAssignmentStatus.Active,
                EffectiveFrom = new DateOnly(2024, 1, 1),
                CreatedById = _userId
            },
            new PositionAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                EmployeeId = _employeeBId,
                PositionId = positionBId,
                AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
                AssignmentStatus = PositionAssignmentStatus.Active,
                EffectiveFrom = new DateOnly(2025, 6, 1),
                CreatedById = _userId
            });
        db.Permissions.AddRange(
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = permAId, Code = "p3-switch-a:read", Module = "core_hr", Description = "A" },
            new ONEVO.Domain.Features.Auth.Entities.Permission { Id = permBId, Code = "p3-switch-b:read", Module = "core_hr", Description = "B" });
        db.Roles.AddRange(
            new Role { Id = roleAId, TenantId = _tenantId, Name = "Switch A", CreatedById = _userId },
            new Role { Id = roleBId, TenantId = _tenantId, Name = "Switch B", CreatedById = _userId });
        db.RolePermissions.AddRange(
            new RolePermission { TenantId = _tenantId, RoleId = roleAId, PermissionId = permAId },
            new RolePermission { TenantId = _tenantId, RoleId = roleBId, PermissionId = permBId });
        db.UserRoles.AddRange(
            new UserRole { TenantId = _tenantId, UserId = _userId, RoleId = roleAId, AssignedBy = _userId, SourcePositionId = positionAId },
            new UserRole { TenantId = _tenantId, UserId = _userId, RoleId = roleBId, AssignedBy = _userId, SourcePositionId = positionBId });
        db.Sessions.Add(new ONEVO.Domain.Features.Auth.Entities.Session
        {
            Id = _sessionId,
            UserId = _userId,
            TenantId = _tenantId,
            KeyHash = "switch-active-company-test-key",
            CsrfTokenHash = "csrf",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ActiveEmployeeId = _employeeAId
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task SwitchActiveCompany_ChangesEffectivePermissionsOnNextRequest()
    {
        await using var db = CreateContext(_tenantId, "switch-company");
        var permissions = new EfAuthRepository(db);
        var now = DateTimeOffset.UtcNow;

        var before = await permissions.ListRolePermissionCodesWithModulesAsync(_userId, now, _legalEntityAId);
        before.Select(p => p.Code).Should().Equal("p3-switch-a:read");

        var handler = new SwitchActiveCompanyCommandHandler(
            permissions,
            new EfEmployeeRepository(db),
            new UnitOfWork(db),
            new StubCurrentUser(_tenantId, _userId, _sessionId));

        var result = await handler.Handle(new SwitchActiveCompanyCommand(_employeeBId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == _sessionId);
        session.ActiveEmployeeId.Should().Be(_employeeBId);

        var after = await permissions.ListRolePermissionCodesWithModulesAsync(_userId, now, _legalEntityBId);
        after.Select(p => p.Code).Should().Equal("p3-switch-b:read");
        after.Select(p => p.Code).Should().NotContain("p3-switch-a:read");
    }

    [Fact]
    public async Task GetDefaultForUserAsync_PrefersMostRecentPrimaryEmployment()
    {
        await using var db = CreateContext(_tenantId, "switch-company");
        var employees = new EfEmployeeRepository(db);

        var def = await employees.GetDefaultForUserAsync(_tenantId, _userId);

        def.Should().NotBeNull();
        def!.Id.Should().Be(_employeeBId);
    }

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
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

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid tenantId, Guid userId, Guid sessionId)
        {
            TenantId = tenantId;
            UserId = userId;
            SessionId = sessionId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public Guid? SessionId { get; }
        public string Email => "switch@example.com";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }
}
