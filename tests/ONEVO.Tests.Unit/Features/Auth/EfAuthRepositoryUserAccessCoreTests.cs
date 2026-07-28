using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for the Phase 1F-D user permission override and user
/// role portion of EfAuthRepository (IUserPermissionOverrideRepository, IUserRoleRepository).
/// Runs on SQLite shared in-memory (via the test-only SqliteTestApplicationDbContext) rather
/// than the EF InMemory provider, matching the pattern used by EfAuthRepositoryAuthCoreTests,
/// EfAuthRepositoryRoleCoreTests, and EfAuthRepositoryPermissionCoreTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
/// Feature-access, audit-log, and role-template methods on EfAuthRepository are out of scope
/// for this phase and are not covered here.
///
/// Note on ordering: none of the methods under test apply an explicit OrderBy, so these tests
/// assert set membership (BeEquivalentTo) rather than sequence, to avoid depending on
/// SQLite's unspecified row order.
/// </summary>
public sealed class EfAuthRepositoryUserAccessCoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfAuthRepositoryUserAccessCoreTests()
    {
        var databaseName = $"auth_repo_user_access_core_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";

        // A named shared in-memory SQLite database lives only while at least one connection to it
        // stays open; this master connection pins it for the duration of the test.
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        _schemaContext = CreateContext();
        _schemaContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _schemaContext.Dispose();
        _masterConnection.Dispose();
    }

    // ---- ListForUserAsync ----

    [Fact]
    public async Task ListForUserAsync_ReturnsOverrideGrantsForRequestedTenantAndUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        await SeedAsync(permission);
        await SeedAsync(NewOverride(tenantId, userId, permission.Id, "grant"));

        var grants = await repo.ListForUserAsync(tenantId, userId);

        grants.Should().ContainSingle();
        grants[0].Code.Should().Be("employees.read");
    }

    [Fact]
    public async Task ListForUserAsync_JoinsToPermissionAndReturnsCode()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var readPermission = NewPermission("payroll.read", "payroll");
        var writePermission = NewPermission("payroll.write", "payroll");
        await SeedAsync(readPermission, writePermission);
        await SeedAsync(
            NewOverride(tenantId, userId, readPermission.Id, "grant"),
            NewOverride(tenantId, userId, writePermission.Id, "revoke"));

        var grants = await repo.ListForUserAsync(tenantId, userId);

        grants.Select(g => g.Code).Should().BeEquivalentTo(new[] { "payroll.read", "payroll.write" });
    }

    [Fact]
    public async Task ListForUserAsync_PreservesGrantType()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var grantedPermission = NewPermission("leave.approve", "leave");
        var revokedPermission = NewPermission("leave.cancel", "leave");
        await SeedAsync(grantedPermission, revokedPermission);
        await SeedAsync(
            NewOverride(tenantId, userId, grantedPermission.Id, "grant"),
            NewOverride(tenantId, userId, revokedPermission.Id, "revoke"));

        var grants = await repo.ListForUserAsync(tenantId, userId);

        grants.Single(g => g.Code == "leave.approve").GrantType.Should().Be("grant");
        grants.Single(g => g.Code == "leave.cancel").GrantType.Should().Be("revoke");
    }

    [Fact]
    public async Task ListForUserAsync_ExcludesOverridesForAnotherTenant()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("attendance.read", "attendance");
        await SeedAsync(permission);
        await SeedAsync(NewOverride(otherTenantId, userId, permission.Id, "grant"));

        var grants = await repo.ListForUserAsync(tenantId, userId);

        grants.Should().BeEmpty();
    }

    [Fact]
    public async Task ListForUserAsync_ExcludesOverridesForAnotherUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var permission = NewPermission("attendance.read", "attendance");
        await SeedAsync(permission);
        await SeedAsync(NewOverride(tenantId, otherUserId, permission.Id, "grant"));

        var grants = await repo.ListForUserAsync(tenantId, userId);

        grants.Should().BeEmpty();
    }

    // ---- ListActiveByUserIdAsync ----

    [Fact]
    public async Task ListActiveByUserIdAsync_ReturnsRolesForUserWhenExpiresAtIsNull()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var userRoles = await repo.ListActiveByUserIdAsync(userId, _clock.UtcNow);

        userRoles.Should().ContainSingle();
        userRoles[0].RoleId.Should().Be(role.Id);
    }

    [Fact]
    public async Task ListActiveByUserIdAsync_ReturnsRolesWhenExpiresAtIsInTheFuture()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(1)));

        var userRoles = await repo.ListActiveByUserIdAsync(userId, _clock.UtcNow);

        userRoles.Should().ContainSingle();
        userRoles[0].RoleId.Should().Be(role.Id);
    }

    [Fact]
    public async Task ListActiveByUserIdAsync_ExcludesExpiredRoles()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(-1)));

        var userRoles = await repo.ListActiveByUserIdAsync(userId, _clock.UtcNow);

        userRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveByUserIdAsync_ExcludesRolesForAnotherUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, otherUserId, role.Id, expiresAt: null));

        var userRoles = await repo.ListActiveByUserIdAsync(userId, _clock.UtcNow);

        userRoles.Should().BeEmpty();
    }

    // ---- ListUserIdsByRoleAsync ----
    //
    // These tests pass the FakeClock's fixed UtcNow explicitly as the `now` parameter, matching
    // the deterministic-clock pattern already used by ListActiveByUserIdAsync. Before this fix,
    // ListUserIdsByRoleAsync read DateTimeOffset.UtcNow directly, which made "future expiry"
    // fixtures anchored to the fixed clock silently stop being future once real wall-clock time
    // passed the fixed date, causing date-sensitive failures.

    [Fact]
    public async Task ListUserIdsByRoleAsync_IncludesAssignmentWhenExpiresAtIsNull()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userId = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().ContainSingle().Which.Should().Be(userId);
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_IncludesAssignmentWhenExpiresAtIsAfterSuppliedNow()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userId = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(1)));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().ContainSingle().Which.Should().Be(userId);
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_ExcludesAssignmentExpiringExactlyAtSuppliedNow()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userId = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().BeEmpty("expires_at must be strictly greater than now to count as active");
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_ExcludesExpiredAssignments()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userId = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(-1)));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_ExcludesOtherRoles()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var otherRole = NewRole(tenantId, "hr-admin");
        var userId = Guid.NewGuid();
        await SeedAsync(role, otherRole);
        await SeedAsync(NewUserRole(tenantId, userId, otherRole.Id, expiresAt: null));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_ReturnsDistinctUserIdsForActiveAssignmentsOnRequestedRole()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(
            NewUserRole(tenantId, userA, role.Id, expiresAt: null),
            NewUserRole(tenantId, userB, role.Id, expiresAt: _clock.UtcNow.AddDays(1)));

        var userIds = await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        userIds.Should().BeEquivalentTo(new[] { userA, userB });
    }

    [Fact]
    public async Task ListUserIdsByRoleAsync_DoesNotTrackReturnedRows()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        var userId = Guid.NewGuid();
        await SeedAsync(role);
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        db.ChangeTracker.Clear();
        await repo.ListUserIdsByRoleAsync(role.Id, _clock.UtcNow);

        db.ChangeTracker.Entries<UserRole>().Should().BeEmpty(
            "ListUserIdsByRoleAsync uses AsNoTracking and projects to Guid, so no UserRole entities should be attached");
    }

    // ---- AddAsync(UserRole) ----

    [Fact]
    public async Task AddAsync_UserRole_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(role);
        var userRole = NewUserRole(tenantId, Guid.NewGuid(), role.Id, expiresAt: null);

        await repo.AddAsync(userRole);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userRole.UserId && ur.RoleId == userRole.RoleId);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userRole.UserId && ur.RoleId == userRole.RoleId);
        persistedAfterSave.Should().NotBeNull();
    }

    // ---- Fixtures ----

    private Permission NewPermission(string code, string module)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = "test permission",
            Module = module
        };
    }

    private Role NewRole(Guid tenantId, string name)
    {
        return new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = "test role",
            CreatedAt = _clock.UtcNow
        };
    }

    private UserRole NewUserRole(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset? expiresAt)
    {
        return new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = _clock.UtcNow,
            AssignedBy = Guid.NewGuid(),
            ExpiresAt = expiresAt
        };
    }

    private UserPermissionOverride NewOverride(Guid tenantId, Guid userId, Guid permissionId, string grantType)
    {
        return new UserPermissionOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            PermissionId = permissionId,
            GrantType = grantType,
            Reason = "test reason",
            GrantedBy = Guid.NewGuid(),
            CreatedAt = _clock.UtcNow
        };
    }

    private async Task SeedAsync(params Permission[] permissions)
    {
        using var db = CreateContext();
        db.Permissions.AddRange(permissions);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params Role[] roles)
    {
        using var db = CreateContext();
        db.Roles.AddRange(roles);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params UserRole[] userRoles)
    {
        using var db = CreateContext();
        db.UserRoles.AddRange(userRoles);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params UserPermissionOverride[] overrides)
    {
        using var db = CreateContext();
        db.UserPermissionOverrides.AddRange(overrides);
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class FakeClock : IDateTimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);
    }
}
