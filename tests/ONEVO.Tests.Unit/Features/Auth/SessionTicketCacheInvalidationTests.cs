using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Sessions;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.UserAccounts;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Improvement task 03: every write that changes what an authentication ticket would contain must
/// evict the cached ticket. Tracked writes go through <see cref="SessionTicketCacheInvalidationInterceptor"/>;
/// bulk ExecuteUpdate writes are evicted by the repository that issues them.
/// </summary>
public sealed class SessionTicketCacheInvalidationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly RecordingTicketCache _cache = new();
    private readonly FixedClock _clock = new();

    public SessionTicketCacheInvalidationTests()
    {
        _connectionString = $"Data Source=ticket_cache_eviction_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();
        using var schema = CreateContext();
        schema.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new SessionTicketCacheInvalidationInterceptor(_cache))
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private async Task<Session> SeedSessionAsync(Guid? userId = null)
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            KeyHash = Guid.NewGuid().ToString("N"),
            StartedAt = _clock.UtcNow,
            LastActivityAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(1),
        };
        using var db = CreateContext();
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        _cache.Clear();
        return session;
    }

    [Fact]
    public async Task AddingASession_EvictsNothing()
    {
        await SeedSessionAsync();
        using var db = CreateContext();
        db.Sessions.Add(new Session { Id = Guid.NewGuid(), KeyHash = "new", ExpiresAt = _clock.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        Assert.Empty(_cache.Calls);
    }

    [Fact]
    public async Task RevokingASessionRow_EvictsThatSession()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();
        var session = await db.Sessions.SingleAsync(s => s.Id == seeded.Id);
        session.IsRevoked = true;
        await db.SaveChangesAsync();

        Assert.Contains($"session:{seeded.KeyHash}", _cache.Calls);
    }

    [Fact]
    public async Task SwitchingTheActiveLegalEntity_EvictsThatSession()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();
        var session = await db.Sessions.SingleAsync(s => s.Id == seeded.Id);
        session.ActiveEmployeeId = Guid.NewGuid();
        await db.SaveChangesAsync();

        Assert.Contains($"session:{seeded.KeyHash}", _cache.Calls);
    }

    [Fact]
    public async Task RenewalOnlyWrite_DropsTheEntryWithoutAGraceWindow()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();
        var session = await db.Sessions.SingleAsync(s => s.Id == seeded.Id);
        session.LastActivityAt = _clock.UtcNow.AddMinutes(1);
        session.ExpiresAt = _clock.UtcNow.AddDays(30);
        await db.SaveChangesAsync();

        Assert.NotEmpty(_cache.Calls);
        Assert.All(_cache.Calls, call => Assert.Equal($"drop:{seeded.KeyHash}", call));
    }

    [Fact]
    public async Task ChangingAUser_EvictsThatUser()
    {
        var user = new User { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "a@b.com", IsActive = true };
        using (var seed = CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }
        _cache.Clear();

        using var db = CreateContext();
        var tracked = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        tracked.IsActive = false;
        await db.SaveChangesAsync();

        Assert.Contains($"user:{user.Id}", _cache.Calls);
    }

    [Fact]
    public async Task DeletingAUser_EvictsThatUser()
    {
        var user = new User { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "gone@b.com", IsActive = true };
        using (var seed = CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }
        _cache.Clear();

        using var db = CreateContext();
        var tracked = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        db.Users.Remove(tracked); // soft delete via SoftDeleteInterceptor
        await db.SaveChangesAsync();

        Assert.Contains($"user:{user.Id}", _cache.Calls);
    }

    [Fact]
    public async Task DeactivatingAPlatformUser_EvictsThatUser()
    {
        var admin = new PlatformUser
        {
            Id = Guid.NewGuid(), Email = "admin@onevo.com", FullName = "Admin", Status = PlatformUser.StatusActive,
        };
        using (var seed = CreateContext())
        {
            seed.PlatformUsers.Add(admin);
            await seed.SaveChangesAsync();
        }
        _cache.Clear();

        using var db = CreateContext();
        var tracked = await db.PlatformUsers.SingleAsync(u => u.Id == admin.Id);
        tracked.Status = PlatformUser.StatusInactive;
        await db.SaveChangesAsync();

        Assert.Contains($"user:{admin.Id}", _cache.Calls);
    }

    [Fact]
    public async Task AssigningARole_EvictsThatUser()
    {
        var userId = Guid.NewGuid();
        using var db = CreateContext();
        db.UserRoles.Add(new UserRole { TenantId = Guid.NewGuid(), UserId = userId, RoleId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        Assert.Contains($"user:{userId}", _cache.Calls);
    }

    [Fact]
    public async Task ChangingRolePermissions_EvictsTheTenant()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext();
        db.RolePermissions.Add(new RolePermission { TenantId = tenantId, RoleId = Guid.NewGuid(), PermissionId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        Assert.Contains($"tenant:{tenantId}", _cache.Calls);
    }

    [Fact]
    public async Task ChangingPlatformRolePermissions_EvictsThePlatformScope()
    {
        using var db = CreateContext();
        db.PlatformRolePermissions.Add(new PlatformRolePermission { RoleId = Guid.NewGuid(), PermissionCode = "tenants:read" });
        await db.SaveChangesAsync();

        Assert.Contains($"tenant:{SessionTicketCacheScopes.Platform}", _cache.Calls);
    }

    [Fact]
    public async Task RevokingAPlatformSessionRow_EvictsThatSession()
    {
        var session = new PlatformUserSession
        {
            Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), TokenHash = "platform-hash",
            CreatedAt = _clock.UtcNow, LastActivityAt = _clock.UtcNow, ExpiresAt = _clock.UtcNow.AddDays(1),
        };
        using (var seed = CreateContext())
        {
            seed.PlatformUserSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        _cache.Clear();

        using var db = CreateContext();
        var tracked = await db.PlatformUserSessions.SingleAsync(s => s.Id == session.Id);
        tracked.RevokedAt = _clock.UtcNow;
        await db.SaveChangesAsync();

        Assert.Contains("session:platform-hash", _cache.Calls);
    }

    [Fact]
    public async Task BulkRevokeOfAUsersSessions_EvictsThatUser()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();

        await new EfSessionRepository(db, _cache).RevokeAllActiveByUserIdAsync(seeded.UserId);

        Assert.Contains($"user:{seeded.UserId}", _cache.Calls);
    }

    [Fact]
    public async Task BulkRevokeExceptCurrent_EvictsThatUser()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();

        await new EfSessionRepository(db, _cache).RevokeAllActiveByUserIdExceptAsync(seeded.UserId, Guid.NewGuid());

        Assert.Contains($"user:{seeded.UserId}", _cache.Calls);
    }

    [Fact]
    public async Task UserAccountRevokeSessionAndRevokeAllAccess_EvictThatUser()
    {
        var seeded = await SeedSessionAsync();
        using var db = CreateContext();
        var accounts = new EfUserAccountRepository(db, _cache);

        await accounts.RevokeSessionAsync(seeded.TenantId, seeded.UserId, seeded.Id);
        Assert.Contains($"user:{seeded.UserId}", _cache.Calls);

        _cache.Clear();
        await accounts.RevokeAllAccessAsync(seeded.TenantId, seeded.UserId, _clock.UtcNow, "test");
        Assert.Contains($"user:{seeded.UserId}", _cache.Calls);
    }

    private sealed class RecordingTicketCache : ISessionTicketStoreCache
    {
        private readonly List<string> _calls = [];
        public IReadOnlyList<string> Calls { get { lock (_calls) return [.. _calls]; } }
        public void Clear() { lock (_calls) _calls.Clear(); }
        private void Record(string call) { lock (_calls) _calls.Add(call); }

        public void EvictSession(string keyHash) => Record($"session:{keyHash}");
        public void EvictUser(Guid userId) => Record($"user:{userId}");
        public void EvictTenant(Guid tenantId) => Record($"tenant:{tenantId}");
        public void EvictAll() => Record("all");
        public void DropSession(string keyHash) => Record($"drop:{keyHash}");

        public DateTimeOffset BeginBuild() => DateTimeOffset.UtcNow;
        public bool TryGet<T>(string keyHash, out T? value) where T : class { value = null; return false; }
        public bool TryGet<T>(string keyHash, out T? value, out DateTimeOffset buildStartedAt) where T : class
        {
            value = null;
            buildStartedAt = default;
            return false;
        }
        public void Set<T>(string keyHash, T value, Guid userId, Guid scopeId, DateTimeOffset buildStartedAt) where T : class { }
        public bool ShouldWriteRenewal(string keyHash) => true;
        public void RecordRenewalWrite(string keyHash) { }
    }

    private sealed class FixedClock : ONEVO.Application.Common.ServiceInterfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
