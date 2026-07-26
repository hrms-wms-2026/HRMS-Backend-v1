using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for the Phase 1F-E Support-core portion of
/// EfAuthRepository (IFeatureAccessGrantRepository, IAuditLogRepository,
/// IRoleTemplateRepository). Runs on SQLite shared in-memory (via the test-only
/// SqliteTestApplicationDbContext) rather than the EF InMemory provider, matching the
/// pattern used by EfAuthRepositoryAuthCoreTests, EfAuthRepositoryRoleCoreTests,
/// EfAuthRepositoryPermissionCoreTests, and EfAuthRepositoryUserAccessCoreTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
///
/// Note on casing: SQLite string equality on the default BINARY collation is
/// case-sensitive, but GetByNameAsync explicitly lowers both sides with ToLower() before
/// comparing, so its case-insensitive behavior below reflects the repository's own
/// normalization rather than provider collation.
/// </summary>
public sealed class EfAuthRepositorySupportCoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfAuthRepositorySupportCoreTests()
    {
        var databaseName = $"auth_repo_support_core_tests_{Guid.NewGuid():N}";
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

    // ---- ListForTenantAsync (IFeatureAccessGrantRepository) ----

    [Fact]
    public async Task ListForTenantAsync_ReturnsOnlyGrantsForRequestedTenant()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var grant = NewFeatureAccessGrant(tenantId, "attendance");
        await SeedAsync(grant);

        var results = await repo.ListForTenantAsync(tenantId);

        results.Select(g => g.Id).Should().Equal(grant.Id);
    }

    [Fact]
    public async Task ListForTenantAsync_ExcludesGrantsForAnotherTenant()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var grant = NewFeatureAccessGrant(tenantId, "attendance");
        var otherGrant = NewFeatureAccessGrant(otherTenantId, "payroll");
        await SeedAsync(grant, otherGrant);

        var results = await repo.ListForTenantAsync(tenantId);

        results.Select(g => g.Id).Should().Equal(grant.Id);
    }

    // ---- AddAsync (IAuditLogRepository) ----

    [Fact]
    public async Task AddAsync_AuditLog_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var auditLog = NewAuditLog();

        await repo.AddAsync(auditLog);

        db.Entry(auditLog).State.Should().Be(EntityState.Added);

        var persisted = await _schemaContext.AuditLogs.FirstOrDefaultAsync(a => a.Id == auditLog.Id);
        persisted.Should().BeNull("AddAsync must not call SaveChangesAsync");
    }

    // ---- IRoleTemplateRepository.GetByIdAsync ----

    [Fact]
    public async Task GetByIdAsync_ReturnsMatchingTemplateById()
    {
        using var db = CreateContext();
        IRoleTemplateRepository repo = new EfAuthRepository(db);
        var template = NewRoleTemplate("HR Manager");
        await SeedAsync(template);

        var found = await repo.GetByIdAsync(template.Id);
        var notFound = await repo.GetByIdAsync(Guid.NewGuid());

        found.Should().NotBeNull();
        found!.Id.Should().Be(template.Id);
        notFound.Should().BeNull();
    }

    // ---- GetByNameAsync ----

    [Fact]
    public async Task GetByNameAsync_TrimsInputBeforeLookup()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var template = NewRoleTemplate("HR Manager");
        await SeedAsync(template);

        var found = await repo.GetByNameAsync("  HR Manager  ");

        found.Should().NotBeNull();
        found!.Id.Should().Be(template.Id);
    }

    [Fact]
    public async Task GetByNameAsync_IsCaseInsensitiveViaToLowerComparison()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var template = NewRoleTemplate("HR Manager");
        await SeedAsync(template);

        var found = await repo.GetByNameAsync("hr manager");

        found.Should().NotBeNull("GetByNameAsync compares both sides with ToLower()");
        found!.Id.Should().Be(template.Id);
    }

    [Fact]
    public async Task GetByNameAsync_ReturnsNullForUnknownName()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var template = NewRoleTemplate("HR Manager");
        await SeedAsync(template);

        var found = await repo.GetByNameAsync("Finance Manager");

        found.Should().BeNull();
    }

    // ---- ListAsync ----

    [Fact]
    public async Task ListAsync_OrdersTemplatesByNameAscending()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var templateC = NewRoleTemplate("C Template");
        var templateA = NewRoleTemplate("A Template");
        var templateB = NewRoleTemplate("B Template");
        await SeedAsync(templateC, templateA, templateB);

        var results = await repo.ListAsync();

        results.Select(t => t.Name).Should().Equal("A Template", "B Template", "C Template");
    }

    // ---- AddAsync (RoleTemplate) ----

    [Fact]
    public async Task AddAsync_RoleTemplate_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var template = NewRoleTemplate("Draft Template");

        await repo.AddAsync(template);

        db.Entry(template).State.Should().Be(EntityState.Added);

        var persisted = await _schemaContext.RoleTemplates.FirstOrDefaultAsync(t => t.Id == template.Id);
        persisted.Should().BeNull("AddAsync must not call SaveChangesAsync");
    }

    // ---- Fixtures ----

    private FeatureAccessGrant NewFeatureAccessGrant(Guid tenantId, string module)
    {
        return new FeatureAccessGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GranteeType = "role",
            GranteeId = Guid.NewGuid(),
            Module = module,
            IsEnabled = true,
            GrantedBy = Guid.NewGuid(),
            CreatedAt = _clock.UtcNow
        };
    }

    private AuditLog NewAuditLog()
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Action = "role.created",
            ResourceType = "role",
            ResourceId = Guid.NewGuid(),
            CreatedAt = _clock.UtcNow
        };
    }

    private RoleTemplate NewRoleTemplate(string name)
    {
        return new RoleTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "test role template",
            ModuleKeysJson = "[]",
            PermissionCodesJson = "[]",
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };
    }

    private async Task SeedAsync(params FeatureAccessGrant[] grants)
    {
        using var db = CreateContext();
        db.FeatureAccessGrants.AddRange(grants);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params RoleTemplate[] templates)
    {
        using var db = CreateContext();
        db.RoleTemplates.AddRange(templates);
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
