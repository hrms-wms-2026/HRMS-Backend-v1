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
/// Provider-independent behavior tests for the Phase 1F-C Permission portion of
/// EfAuthRepository (IPermissionRepository). Runs on SQLite shared in-memory (via the
/// test-only SqliteTestApplicationDbContext) rather than the EF InMemory provider, matching
/// the pattern used by EfAuthRepositoryAuthCoreTests and EfAuthRepositoryRoleCoreTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
/// User permission override, user-role, feature-access, audit-log, and role-template methods
/// on EfAuthRepository are out of scope for this phase and are not covered here.
///
/// Note on casing: SQLite string equality (==) on the default BINARY collation used by these
/// columns is case-sensitive, so GetByCodeAsync's exact-match casing test below reflects that
/// provider behavior rather than any normalization performed by the repository itself.
/// </summary>
public sealed class EfAuthRepositoryPermissionCoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfAuthRepositoryPermissionCoreTests()
    {
        var databaseName = $"auth_repo_permission_core_tests_{Guid.NewGuid():N}";
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

    // ---- GetByCodeAsync ----

    [Fact]
    public async Task GetByCodeAsync_ReturnsPermissionByExactCode()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("employees.read", "employees");
        await SeedAsync(permission);

        var found = await repo.GetByCodeAsync("employees.read");
        var notFound = await repo.GetByCodeAsync("employees.write");

        found.Should().NotBeNull();
        found!.Id.Should().Be(permission.Id);
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_DoesNotMatchDifferentCasing()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("employees.read", "employees");
        await SeedAsync(permission);

        var found = await repo.GetByCodeAsync("EMPLOYEES.READ");

        found.Should().BeNull("SQLite's default BINARY collation makes == case-sensitive");
    }

    // ---- GetByIdsAsync ----

    [Fact]
    public async Task GetByIdsAsync_ReturnsOnlyRequestedIds()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var match1 = NewPermission("leave.read", "leave");
        var match2 = NewPermission("leave.write", "leave");
        var other = NewPermission("payroll.read", "payroll");
        await SeedAsync(match1, match2, other);

        var results = await repo.GetByIdsAsync(new[] { match1.Id, match2.Id });

        results.Select(p => p.Id).Should().BeEquivalentTo(new[] { match1.Id, match2.Id });
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsEmptyForEmptyInput()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        await SeedAsync(NewPermission("attendance.read", "attendance"));

        var results = await repo.GetByIdsAsync(Array.Empty<Guid>());

        results.Should().BeEmpty();
    }

    // ---- GetByCodesAsync ----

    [Fact]
    public async Task GetByCodesAsync_TrimsInputCodes()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("leave.approve", "leave");
        await SeedAsync(permission);

        var results = await repo.GetByCodesAsync(new[] { "  leave.approve  " });

        results.Select(p => p.Id).Should().Equal(permission.Id);
    }

    [Fact]
    public async Task GetByCodesAsync_IgnoresNullEmptyAndWhiteSpaceInput()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("leave.cancel", "leave");
        await SeedAsync(permission);

        var codesToQuery = new List<string?> { null, string.Empty, "   ", "leave.cancel" };
        var results = await repo.GetByCodesAsync(codesToQuery!);

        results.Select(p => p.Id).Should().Equal(permission.Id);
    }

    [Fact]
    public async Task GetByCodesAsync_DeduplicatesWithOrdinalComparer()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("leave.reject", "leave");
        await SeedAsync(permission);

        var results = await repo.GetByCodesAsync(new[] { "leave.reject", "leave.reject", " leave.reject" });

        results.Select(p => p.Id).Should().Equal(permission.Id);
    }

    [Fact]
    public async Task GetByCodesAsync_DoesNotLowerCaseCodes()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var permission = NewPermission("Leave.Approve", "leave");
        await SeedAsync(permission);

        var found = await repo.GetByCodesAsync(new[] { "Leave.Approve" });
        var notFound = await repo.GetByCodesAsync(new[] { "leave.approve" });

        found.Select(p => p.Id).Should().Equal(permission.Id);
        notFound.Should().BeEmpty("the repository must not lower-case codes before matching");
    }

    // ---- UserHasPermissionCodeAsync ----

    [Fact]
    public async Task UserHasPermissionCodeAsync_ReturnsTrueForActiveUserRoleGrantingCode()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var hasPermission = await repo.UserHasPermissionCodeAsync(userId, "employees.read", _clock.UtcNow);

        hasPermission.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasPermissionCodeAsync_ReturnsFalseWhenUserRoleExpired()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(-1)));

        var hasPermission = await repo.UserHasPermissionCodeAsync(userId, "employees.read", _clock.UtcNow);

        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task UserHasPermissionCodeAsync_ReturnsFalseForDifferentPermissionCode()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var hasPermission = await repo.UserHasPermissionCodeAsync(userId, "employees.write", _clock.UtcNow);

        hasPermission.Should().BeFalse();
    }

    [Fact]
    public async Task UserHasPermissionCodeAsync_ReturnsFalseForDifferentUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var hasPermission = await repo.UserHasPermissionCodeAsync(otherUserId, "employees.read", _clock.UtcNow);

        hasPermission.Should().BeFalse();
    }

    // ---- ListRolePermissionCodesAsync ----

    [Fact]
    public async Task ListRolePermissionCodesAsync_ReturnsDistinctCodesForActiveUserRoles()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var readPermission = NewPermission("employees.read", "employees");
        var writePermission = NewPermission("employees.write", "employees");
        var roleA = NewRole(tenantId, "role-a");
        var roleB = NewRole(tenantId, "role-b");
        await SeedAsync(readPermission, writePermission);
        await SeedAsync(roleA, roleB);
        await SeedAsync(
            NewRolePermission(tenantId, roleA.Id, readPermission.Id),
            NewRolePermission(tenantId, roleB.Id, readPermission.Id),
            NewRolePermission(tenantId, roleB.Id, writePermission.Id));
        await SeedAsync(
            NewUserRole(tenantId, userId, roleA.Id, expiresAt: null),
            NewUserRole(tenantId, userId, roleB.Id, expiresAt: null));

        var codes = await repo.ListRolePermissionCodesAsync(userId, _clock.UtcNow);

        codes.Should().BeEquivalentTo(new[] { "employees.read", "employees.write" });
    }

    [Fact]
    public async Task ListRolePermissionCodesAsync_ExcludesExpiredUserRoles()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(-1)));

        var codes = await repo.ListRolePermissionCodesAsync(userId, _clock.UtcNow);

        codes.Should().BeEmpty();
    }

    // ---- ListRolePermissionCodesWithModulesAsync ----

    [Fact]
    public async Task ListRolePermissionCodesWithModulesAsync_ReturnsCodeAndModulePairs()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: null));

        var pairs = await repo.ListRolePermissionCodesWithModulesAsync(userId, _clock.UtcNow, null);

        pairs.Should().ContainSingle();
        pairs[0].Code.Should().Be("employees.read");
        pairs[0].Module.Should().Be("employees");
    }

    [Fact]
    public async Task ListRolePermissionCodesWithModulesAsync_ReturnsDistinctPairs()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var roleA = NewRole(tenantId, "role-a");
        var roleB = NewRole(tenantId, "role-b");
        await SeedAsync(permission);
        await SeedAsync(roleA, roleB);
        await SeedAsync(
            NewRolePermission(tenantId, roleA.Id, permission.Id),
            NewRolePermission(tenantId, roleB.Id, permission.Id));
        await SeedAsync(
            NewUserRole(tenantId, userId, roleA.Id, expiresAt: null),
            NewUserRole(tenantId, userId, roleB.Id, expiresAt: null));

        var pairs = await repo.ListRolePermissionCodesWithModulesAsync(userId, _clock.UtcNow, null);

        pairs.Should().ContainSingle();
    }

    [Fact]
    public async Task ListRolePermissionCodesWithModulesAsync_ExcludesExpiredUserRoles()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var permission = NewPermission("employees.read", "employees");
        var role = NewRole(tenantId, "hr-user");
        await SeedAsync(permission);
        await SeedAsync(role);
        await SeedAsync(NewRolePermission(tenantId, role.Id, permission.Id));
        await SeedAsync(NewUserRole(tenantId, userId, role.Id, expiresAt: _clock.UtcNow.AddDays(-1)));

        var pairs = await repo.ListRolePermissionCodesWithModulesAsync(userId, _clock.UtcNow, null);

        pairs.Should().BeEmpty();
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

    private RolePermission NewRolePermission(Guid tenantId, Guid roleId, Guid permissionId)
    {
        return new RolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permissionId
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

    private async Task SeedAsync(params RolePermission[] rolePermissions)
    {
        using var db = CreateContext();
        db.RolePermissions.AddRange(rolePermissions);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params UserRole[] userRoles)
    {
        using var db = CreateContext();
        db.UserRoles.AddRange(userRoles);
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
