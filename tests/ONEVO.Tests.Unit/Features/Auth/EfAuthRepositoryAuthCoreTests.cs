using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for the Phase 1F-A auth/core portion of
/// EfAuthRepository: user, refresh token, session, password reset token, and MFA lookup/add/
/// revoke methods. Runs on SQLite shared in-memory (via the test-only
/// SqliteTestApplicationDbContext) rather than the EF InMemory provider, matching the pattern
/// used by EfInvitationTokenRepositoryTests and PostgresMfaChallengeStoreTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
/// Role, permission, user-role, feature-access, audit-log, and role-template methods on
/// EfAuthRepository are out of scope for this phase and are not covered here.
/// </summary>
public sealed class EfAuthRepositoryAuthCoreTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfAuthRepositoryAuthCoreTests()
    {
        var databaseName = $"auth_repo_core_tests_{Guid.NewGuid():N}";
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

    // ---- User ----

    [Fact]
    public async Task GetByNormalizedEmailAsync_ReturnsMatchingNonDeletedUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var user = NewUser(Guid.NewGuid(), "match@example.com");
        await SeedAsync(user);

        var found = await repo.GetByNormalizedEmailAsync("match@example.com");

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByNormalizedEmailAsync_DoesNotReturnDeletedUser()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var user = NewUser(Guid.NewGuid(), "deleted@example.com");
        user.IsDeleted = true;
        await SeedAsync(user);

        var found = await repo.GetByNormalizedEmailAsync("deleted@example.com");

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByNormalizedEmailAsync_RequiresActiveAndNonDeleted()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var inactive = NewUser(Guid.NewGuid(), "inactive@example.com");
        inactive.IsActive = false;
        var deleted = NewUser(Guid.NewGuid(), "deleted-active@example.com");
        deleted.IsDeleted = true;
        var active = NewUser(Guid.NewGuid(), "active@example.com");
        await SeedAsync(inactive, deleted, active);

        var foundInactive = await repo.GetActiveByNormalizedEmailAsync("inactive@example.com");
        var foundDeleted = await repo.GetActiveByNormalizedEmailAsync("deleted-active@example.com");
        var foundActive = await repo.GetActiveByNormalizedEmailAsync("active@example.com");

        foundInactive.Should().BeNull();
        foundDeleted.Should().BeNull();
        foundActive.Should().NotBeNull();
        foundActive!.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task GetByIdAsync_RequiresMatchingIdAndNonDeleted()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var user = NewUser(Guid.NewGuid(), "byid@example.com");
        var deletedUser = NewUser(Guid.NewGuid(), "byid-deleted@example.com");
        deletedUser.IsDeleted = true;
        await SeedAsync(user, deletedUser);

        var found = await repo.GetByIdAsync(user.Id);
        var foundDeleted = await repo.GetByIdAsync(deletedUser.Id);
        var foundMissing = await repo.GetByIdAsync(Guid.NewGuid());

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        foundDeleted.Should().BeNull();
        foundMissing.Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantAndEmailAsync_RequiresBothTenantIdAndNormalizedEmail()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var user = NewUser(tenantId, "tenant-scoped@example.com");
        var otherTenantUser = NewUser(otherTenantId, "tenant-scoped@example.com");
        await SeedAsync(user, otherTenantUser);

        var found = await repo.GetByTenantAndEmailAsync(tenantId, "tenant-scoped@example.com");
        var notFoundWrongTenant = await repo.GetByTenantAndEmailAsync(Guid.NewGuid(), "tenant-scoped@example.com");

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        notFoundWrongTenant.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_User_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var user = NewUser(Guid.NewGuid(), "new-user@example.com");

        await repo.AddAsync(user);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    // ---- Refresh token ----

    [Fact]
    public async Task GetByHashAsync_LooksUpByTokenHashExactly()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var token = NewRefreshToken(Guid.NewGuid());
        token.TokenHash = "expected-refresh-hash";
        await SeedAsync(token);

        var foundByHash = await repo.GetByHashAsync("expected-refresh-hash");
        var foundByWrongHash = await repo.GetByHashAsync("wrong-hash");

        foundByHash.Should().NotBeNull();
        foundByHash!.Id.Should().Be(token.Id);
        foundByWrongHash.Should().BeNull();
    }

    [Fact]
    public async Task ListActiveByUserIdAsync_RefreshToken_ReturnsOnlyRequestedUserAndUnrevoked()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var active = NewRefreshToken(userId);
        var revoked = NewRefreshToken(userId);
        revoked.RevokedAt = _clock.UtcNow;
        var otherUserActive = NewRefreshToken(otherUserId);
        await SeedAsync(active, revoked, otherUserActive);

        var results = await repo.ListActiveByUserIdAsync(userId);

        results.Select(t => t.Id).Should().Equal(active.Id);
    }

    [Fact]
    public async Task AddAsync_RefreshToken_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var token = NewRefreshToken(Guid.NewGuid());

        await repo.AddAsync(token);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.RefreshTokens.FirstOrDefaultAsync(t => t.Id == token.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.RefreshTokens.FirstOrDefaultAsync(t => t.Id == token.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    // ---- Session ----

    [Fact]
    public async Task GetLatestActiveByUserIdAsync_ReturnsLatestNonRevokedSessionByLastActivityAt()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var userId = Guid.NewGuid();

        var older = NewSession(userId);
        older.LastActivityAt = _clock.UtcNow.AddMinutes(-10);
        var newer = NewSession(userId);
        newer.LastActivityAt = _clock.UtcNow.AddMinutes(-1);
        var newestButRevoked = NewSession(userId);
        newestButRevoked.LastActivityAt = _clock.UtcNow;
        newestButRevoked.IsRevoked = true;
        await SeedAsync(older, newer, newestButRevoked);

        var found = await repo.GetLatestActiveByUserIdAsync(userId);

        found.Should().NotBeNull();
        found!.Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task ISessionRepository_GetByIdAsync_ReturnsById()
    {
        using var db = CreateContext();
        ISessionRepository repo = new EfAuthRepository(db);
        var session = NewSession(Guid.NewGuid());
        await SeedAsync(session);

        var found = await repo.GetByIdAsync(session.Id);
        var notFound = await repo.GetByIdAsync(Guid.NewGuid());

        found.Should().NotBeNull();
        found!.Id.Should().Be(session.Id);
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task ISessionRepository_GetByKeyHashAsync_ReturnsByKeyHash()
    {
        using var db = CreateContext();
        ISessionRepository repo = new EfAuthRepository(db);
        var session = NewSession(Guid.NewGuid());
        session.KeyHash = "expected-session-key-hash";
        await SeedAsync(session);

        var found = await repo.GetByKeyHashAsync("expected-session-key-hash");
        var notFound = await repo.GetByKeyHashAsync("wrong-key-hash");

        found.Should().NotBeNull();
        found!.Id.Should().Be(session.Id);
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task RevokeByKeyHashAsync_MarksOnlyMatchingSessionRevokedAndDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        ISessionRepository repo = new EfAuthRepository(db);
        var target = NewSession(Guid.NewGuid());
        target.KeyHash = "revoke-target-key-hash";
        var other = NewSession(Guid.NewGuid());
        other.KeyHash = "other-key-hash";
        await SeedAsync(target, other);

        await repo.RevokeByKeyHashAsync("revoke-target-key-hash");

        using var verificationDb = CreateContext();
        var persistedTarget = await verificationDb.Sessions.FirstOrDefaultAsync(s => s.Id == target.Id);
        var persistedOther = await verificationDb.Sessions.FirstOrDefaultAsync(s => s.Id == other.Id);
        persistedTarget!.IsRevoked.Should().BeFalse("RevokeByKeyHashAsync must not save automatically");
        persistedOther!.IsRevoked.Should().BeFalse();

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var targetAfterSave = await verificationDbAfterSave.Sessions.FirstOrDefaultAsync(s => s.Id == target.Id);
        var otherAfterSave = await verificationDbAfterSave.Sessions.FirstOrDefaultAsync(s => s.Id == other.Id);
        targetAfterSave!.IsRevoked.Should().BeTrue();
        otherAfterSave!.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeByIdAsync_MarksOnlyMatchingSessionRevokedAndDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        ISessionRepository repo = new EfAuthRepository(db);
        var target = NewSession(Guid.NewGuid());
        var other = NewSession(Guid.NewGuid());
        await SeedAsync(target, other);

        await repo.RevokeByIdAsync(target.Id);

        using var verificationDb = CreateContext();
        var persistedTarget = await verificationDb.Sessions.FirstOrDefaultAsync(s => s.Id == target.Id);
        var persistedOther = await verificationDb.Sessions.FirstOrDefaultAsync(s => s.Id == other.Id);
        persistedTarget!.IsRevoked.Should().BeFalse("RevokeByIdAsync must not save automatically");
        persistedOther!.IsRevoked.Should().BeFalse();

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var targetAfterSave = await verificationDbAfterSave.Sessions.FirstOrDefaultAsync(s => s.Id == target.Id);
        var otherAfterSave = await verificationDbAfterSave.Sessions.FirstOrDefaultAsync(s => s.Id == other.Id);
        targetAfterSave!.IsRevoked.Should().BeTrue();
        otherAfterSave!.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_Session_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var session = NewSession(Guid.NewGuid());

        await repo.AddAsync(session);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.Sessions.FirstOrDefaultAsync(s => s.Id == session.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.Sessions.FirstOrDefaultAsync(s => s.Id == session.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    // ---- Password reset token ----

    [Fact]
    public async Task GetResetTokenByHashAsync_LooksUpByTokenHashExactly()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var token = NewPasswordResetToken(Guid.NewGuid());
        token.TokenHash = "expected-reset-hash";
        await SeedAsync(token);

        var foundByHash = await repo.GetResetTokenByHashAsync("expected-reset-hash");
        var foundByWrongHash = await repo.GetResetTokenByHashAsync("wrong-hash");

        foundByHash.Should().NotBeNull();
        foundByHash!.Id.Should().Be(token.Id);
        foundByWrongHash.Should().BeNull();
    }

    [Fact]
    public async Task ListValidByUserIdAsync_ReturnsOnlyRequestedUserUnusedAndUnexpired()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var userId = Guid.NewGuid();

        var valid = NewPasswordResetToken(userId);
        var used = NewPasswordResetToken(userId);
        used.UsedAt = _clock.UtcNow;
        var expired = NewPasswordResetToken(userId);
        expired.ExpiresAt = _clock.UtcNow.AddMinutes(-1);
        var otherUserValid = NewPasswordResetToken(Guid.NewGuid());
        await SeedAsync(valid, used, expired, otherUserValid);

        var results = await repo.ListValidByUserIdAsync(userId, _clock.UtcNow);

        results.Select(t => t.Id).Should().Equal(valid.Id);
    }

    [Fact]
    public async Task AddAsync_PasswordResetToken_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var token = NewPasswordResetToken(Guid.NewGuid());

        await repo.AddAsync(token);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.PasswordResetTokens.FirstOrDefaultAsync(t => t.Id == token.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave =
            await verificationDbAfterSave.PasswordResetTokens.FirstOrDefaultAsync(t => t.Id == token.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    // ---- MFA ----

    [Fact]
    public async Task GetTotpAsync_RequiresUserIdMethodTypeTotpAndMatchingIsVerified()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var userId = Guid.NewGuid();

        var verifiedTotp = NewUserMfa(userId, "totp");
        verifiedTotp.IsVerified = true;
        var otherMethod = NewUserMfa(userId, "email_otp");
        otherMethod.IsVerified = true;
        var otherUserTotp = NewUserMfa(Guid.NewGuid(), "totp");
        otherUserTotp.IsVerified = true;
        await SeedAsync(verifiedTotp, otherMethod, otherUserTotp);

        var foundVerified = await repo.GetTotpAsync(userId, isVerified: true);
        var foundUnverified = await repo.GetTotpAsync(userId, isVerified: false);

        foundVerified.Should().NotBeNull();
        foundVerified!.Id.Should().Be(verifiedTotp.Id);
        foundUnverified.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_UserMfa_AddsButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var mfa = NewUserMfa(Guid.NewGuid(), "totp");

        await repo.AddAsync(mfa);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.UserMfas.FirstOrDefaultAsync(m => m.Id == mfa.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave = await verificationDbAfterSave.UserMfas.FirstOrDefaultAsync(m => m.Id == mfa.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_UserMfa_RemovesEntityFromDbSet()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);
        var mfa = NewUserMfa(Guid.NewGuid(), "totp");
        await SeedAsync(mfa);

        var tracked = await db.UserMfas.FirstAsync(m => m.Id == mfa.Id);
        repo.Remove(tracked);
        await db.SaveChangesAsync();

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.UserMfas.FirstOrDefaultAsync(m => m.Id == mfa.Id);
        persisted.Should().BeNull();
    }

    // ---- Fixtures ----

    private User NewUser(Guid tenantId, string email)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            PasswordHash = "hash",
            FirstName = "Test",
            LastName = "User",
            CreatedAt = _clock.UtcNow
        };
    }

    private RefreshToken NewRefreshToken(Guid userId)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            ExpiresAt = _clock.UtcNow.AddDays(7),
            CreatedAt = _clock.UtcNow
        };
    }

    private Session NewSession(Guid userId)
    {
        return new Session
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            IpAddress = "127.0.0.1",
            UserAgent = "test-agent",
            StartedAt = _clock.UtcNow,
            LastActivityAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddHours(1),
            CsrfTokenHash = $"csrf-{Guid.NewGuid():N}",
            KeyHash = $"key-{Guid.NewGuid():N}"
        };
    }

    private PasswordResetToken NewPasswordResetToken(Guid userId)
    {
        return new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            ExpiresAt = _clock.UtcNow.AddHours(1),
            CreatedAt = _clock.UtcNow
        };
    }

    private UserMfa NewUserMfa(Guid userId, string methodType)
    {
        return new UserMfa
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            MethodType = methodType,
            Secret = "secret",
            CreatedAt = _clock.UtcNow
        };
    }

    private async Task SeedAsync(params User[] users)
    {
        using var db = CreateContext();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params RefreshToken[] tokens)
    {
        using var db = CreateContext();
        db.RefreshTokens.AddRange(tokens);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params Session[] sessions)
    {
        using var db = CreateContext();
        db.Sessions.AddRange(sessions);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params PasswordResetToken[] tokens)
    {
        using var db = CreateContext();
        db.PasswordResetTokens.AddRange(tokens);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params UserMfa[] mfas)
    {
        using var db = CreateContext();
        db.UserMfas.AddRange(mfas);
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
        private readonly DateTimeOffset _utcNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);
    }
}
