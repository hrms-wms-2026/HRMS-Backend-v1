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
/// Provider-independent behavior tests for the Phase 1F-B Role and RolePermission portion of
/// EfAuthRepository (IRoleRepository, IRolePermissionRepository). Runs on SQLite shared
/// in-memory (via the test-only SqliteTestApplicationDbContext) rather than the EF InMemory
/// provider, matching the pattern used by EfAuthRepositoryAuthCoreTests and
/// EfInvitationTokenRepositoryTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
/// Permission, user-role, feature-access, audit-log, and role-template methods on
/// EfAuthRepository are out of scope for this phase and are not covered here.
/// </summary>
public sealed class EfAuthRepositoryRoleCoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfAuthRepositoryRoleCoreTests()
    {
        var databaseName = $"auth_repo_role_core_tests_{Guid.NewGuid():N}";
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

    // ---- Role ----

    [Fact]
    public async Task GetRoleByIdAsync_ReturnsMatchingRoleById()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var role = NewRole(Guid.NewGuid(), "manager");
        await SeedAsync(role);

        var found = await repo.GetRoleByIdAsync(role.Id);
        var notFound = await repo.GetRoleByIdAsync(Guid.NewGuid());

        found.Should().NotBeNull();
        found!.Id.Should().Be(role.Id);
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdForTenantAsync_RequiresBothTenantIdAndRoleId()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "supervisor");
        await SeedAsync(role);

        var found = await repo.GetByIdForTenantAsync(tenantId, role.Id);
        var notFoundWrongTenant = await repo.GetByIdForTenantAsync(Guid.NewGuid(), role.Id);

        found.Should().NotBeNull();
        found!.Id.Should().Be(role.Id);
        notFoundWrongTenant.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdForTenantAsync_DoesNotReturnSameRoleIdFromAnotherTenant()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "lead");
        await SeedAsync(role);

        var foundFromOwnTenant = await repo.GetByIdForTenantAsync(tenantId, role.Id);
        var foundFromOtherTenant = await repo.GetByIdForTenantAsync(otherTenantId, role.Id);

        foundFromOwnTenant.Should().NotBeNull();
        foundFromOtherTenant.Should().BeNull();
    }

    [Fact]
    public async Task GetByNameForTenantAsync_RequiresTenantIdAndExactName()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "hr-admin");
        var otherTenantRole = NewRole(otherTenantId, "hr-admin");
        await SeedAsync(role, otherTenantRole);

        var found = await repo.GetByNameForTenantAsync(tenantId, "hr-admin");
        var notFoundWrongTenant = await repo.GetByNameForTenantAsync(Guid.NewGuid(), "hr-admin");
        var notFoundWrongName = await repo.GetByNameForTenantAsync(tenantId, "not-a-role");

        found.Should().NotBeNull();
        found!.Id.Should().Be(role.Id);
        notFoundWrongTenant.Should().BeNull();
        notFoundWrongName.Should().BeNull();
    }

    [Fact]
    public async Task GetBySourceTemplateForTenantAsync_RequiresTenantIdAndSourceTemplateId()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var role = NewRole(tenantId, "templated-role");
        role.SourceTemplateId = templateId;
        var roleWithoutTemplate = NewRole(tenantId, "untemplated-role");
        await SeedAsync(role, roleWithoutTemplate);

        var found = await repo.GetBySourceTemplateForTenantAsync(tenantId, templateId);
        var notFoundWrongTemplate = await repo.GetBySourceTemplateForTenantAsync(tenantId, Guid.NewGuid());
        var notFoundWrongTenant = await repo.GetBySourceTemplateForTenantAsync(Guid.NewGuid(), templateId);

        found.Should().NotBeNull();
        found!.Id.Should().Be(role.Id);
        notFoundWrongTemplate.Should().BeNull();
        notFoundWrongTenant.Should().BeNull();
    }

    [Fact]
    public async Task ListByTenantAsync_ReturnsOnlyRequestedTenantRolesOrderedByNameAscending()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var charlie = NewRole(tenantId, "charlie");
        var alpha = NewRole(tenantId, "alpha");
        var bravo = NewRole(tenantId, "bravo");
        var otherTenantRole = NewRole(otherTenantId, "aardvark");
        await SeedAsync(charlie, alpha, bravo, otherTenantRole);

        var results = await repo.ListByTenantAsync(tenantId);

        results.Select(r => r.Name).Should().Equal("alpha", "bravo", "charlie");
    }

    [Fact]
    public async Task AddAsync_Role_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var role = NewRole(Guid.NewGuid(), "new-role");

        await repo.AddAsync(role);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_Role_RemovesFromDbSetButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var role = NewRole(Guid.NewGuid(), "removable-role");
        await SeedAsync(role);

        var tracked = await db.Roles.FirstAsync(r => r.Id == role.Id);
        repo.Remove(tracked);

        using var verificationDb = CreateContext();
        var persistedBeforeSave = await verificationDb.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        persistedBeforeSave.Should().NotBeNull("Remove must not call SaveChangesAsync automatically");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.Roles.FirstOrDefaultAsync(r => r.Id == role.Id);
        persistedAfterSave.Should().BeNull();
    }

    // ---- RolePermission ----

    [Fact]
    public async Task ListByRoleAsync_ReturnsOnlyRolePermissionsForRequestedRoleId()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "role-with-permissions");
        var otherRole = NewRole(tenantId, "other-role");
        await SeedAsync(role, otherRole);

        var matching1 = NewRolePermission(tenantId, role.Id);
        var matching2 = NewRolePermission(tenantId, role.Id);
        var otherRolePermission = NewRolePermission(tenantId, otherRole.Id);
        await SeedAsync(matching1, matching2, otherRolePermission);

        var results = await repo.ListByRoleAsync(role.Id);

        results.Select(rp => rp.PermissionId).Should().BeEquivalentTo(
            new[] { matching1.PermissionId, matching2.PermissionId });
    }

    [Fact]
    public async Task AddRangeAsync_RolePermissions_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "role-for-add-range");
        await SeedAsync(role);

        var rolePermissions = new[]
        {
            NewRolePermission(tenantId, role.Id),
            NewRolePermission(tenantId, role.Id),
        };

        await repo.AddRangeAsync(rolePermissions);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync();
        persisted.Should().BeEmpty("AddRangeAsync must stage the entities without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync();
        persistedAfterSave.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveRange_RolePermissions_RemovesFromDbSetButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var role = NewRole(tenantId, "role-for-remove-range");
        await SeedAsync(role);

        var toRemove1 = NewRolePermission(tenantId, role.Id);
        var toRemove2 = NewRolePermission(tenantId, role.Id);
        var toKeep = NewRolePermission(tenantId, role.Id);
        await SeedAsync(toRemove1, toRemove2, toKeep);

        var tracked = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id
                && (rp.PermissionId == toRemove1.PermissionId || rp.PermissionId == toRemove2.PermissionId))
            .ToListAsync();
        repo.RemoveRange(tracked);

        using var verificationDb = CreateContext();
        var persistedBeforeSave = await verificationDb.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync();
        persistedBeforeSave.Should().HaveCount(3, "RemoveRange must not call SaveChangesAsync automatically");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync();
        persistedAfterSave.Select(rp => rp.PermissionId).Should().Equal(toKeep.PermissionId);
    }

    // ---- Fixtures ----

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

    private RolePermission NewRolePermission(Guid tenantId, Guid roleId)
    {
        return new RolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = Guid.NewGuid()
        };
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
