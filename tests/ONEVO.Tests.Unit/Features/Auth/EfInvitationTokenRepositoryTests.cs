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
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for EfInvitationTokenRepository: predicate scoping
/// (tenant, email, used/revoked/expired), ordering, and tracking behavior. Runs on SQLite
/// shared in-memory (via the test-only SqliteTestApplicationDbContext) rather than the EF
/// InMemory provider, matching the pattern used by PostgresMfaChallengeStoreTests.
///
/// These are repository predicate/tracking/order tests, not PostgreSQL translation tests.
/// </summary>
public sealed class EfInvitationTokenRepositoryTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly FakeClock _clock = new();

    public EfInvitationTokenRepositoryTests()
    {
        var databaseName = $"invitation_token_repo_tests_{Guid.NewGuid():N}";
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

    [Fact]
    public async Task GetByIdAsync_ReturnsMatchingInviteToken()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var token = NewToken(tenantId: Guid.NewGuid(), email: "person@example.com");
        await SeedAsync(token);

        var found = await repo.GetByIdAsync(token.Id);

        found.Should().NotBeNull();
        found!.Id.Should().Be(token.Id);
    }

    [Fact]
    public async Task GetActiveByEmailAsync_ReturnsOnlyTenantEmailUnusedUnrevokedUnexpiredToken()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var active = NewToken(tenantId, "match@example.com");
        await SeedAsync(active);

        var found = await repo.GetActiveByEmailAsync(tenantId, "match@example.com", _clock.UtcNow);

        found.Should().NotBeNull();
        found!.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task GetActiveByEmailAsync_DoesNotReturnInviteFromAnotherTenant()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherTenantToken = NewToken(otherTenantId, "match@example.com");
        await SeedAsync(otherTenantToken);

        var found = await repo.GetActiveByEmailAsync(tenantId, "match@example.com", _clock.UtcNow);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByEmailAsync_DoesNotReturnUsedInvites()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var used = NewToken(tenantId, "match@example.com");
        used.UsedAt = _clock.UtcNow;
        await SeedAsync(used);

        var found = await repo.GetActiveByEmailAsync(tenantId, "match@example.com", _clock.UtcNow);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByEmailAsync_DoesNotReturnRevokedInvites()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var revoked = NewToken(tenantId, "match@example.com");
        revoked.RevokedAt = _clock.UtcNow;
        await SeedAsync(revoked);

        var found = await repo.GetActiveByEmailAsync(tenantId, "match@example.com", _clock.UtcNow);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveByEmailAsync_DoesNotReturnExpiredInvites()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var expired = NewToken(tenantId, "match@example.com");
        expired.ExpiresAt = _clock.UtcNow.AddMinutes(-1);
        await SeedAsync(expired);

        var found = await repo.GetActiveByEmailAsync(tenantId, "match@example.com", _clock.UtcNow);

        found.Should().BeNull();
    }

    [Fact]
    public async Task CountActiveByTenantAsync_CountsOnlyActiveInvitesForRequestedTenant()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var activeOne = NewToken(tenantId, "one@example.com");
        var activeTwo = NewToken(tenantId, "two@example.com");
        var used = NewToken(tenantId, "used@example.com");
        used.UsedAt = _clock.UtcNow;
        var revoked = NewToken(tenantId, "revoked@example.com");
        revoked.RevokedAt = _clock.UtcNow;
        var expired = NewToken(tenantId, "expired@example.com");
        expired.ExpiresAt = _clock.UtcNow.AddMinutes(-1);
        var otherTenantActive = NewToken(otherTenantId, "other@example.com");
        await SeedAsync(activeOne, activeTwo, used, revoked, expired, otherTenantActive);

        var count = await repo.CountActiveByTenantAsync(tenantId, _clock.UtcNow);

        count.Should().Be(2);
    }

    [Fact]
    public async Task HasAcceptedInviteByTenantAsync_ReturnsTrueOnlyWhenAnInviteForThatTenantHasUsedAtSet()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var withoutAccepted = NewToken(tenantId, "pending@example.com");
        await SeedAsync(withoutAccepted);

        var beforeAcceptance = await repo.HasAcceptedInviteByTenantAsync(tenantId);

        var accepted = NewToken(tenantId, "accepted@example.com");
        accepted.UsedAt = _clock.UtcNow;
        await SeedAsync(accepted);

        var afterAcceptance = await repo.HasAcceptedInviteByTenantAsync(tenantId);

        beforeAcceptance.Should().BeFalse();
        afterAcceptance.Should().BeTrue();
    }

    [Fact]
    public async Task HasAcceptedInviteByTenantAsync_IgnoresAcceptedInvitesFromAnotherTenant()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherTenantAccepted = NewToken(otherTenantId, "accepted@example.com");
        otherTenantAccepted.UsedAt = _clock.UtcNow;
        await SeedAsync(otherTenantAccepted);

        var hasAccepted = await repo.HasAcceptedInviteByTenantAsync(tenantId);

        hasAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task ListByTenantAsync_ReturnsOnlyRequestedTenantOrderedByCreatedAtDescending()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var oldest = NewToken(tenantId, "oldest@example.com");
        oldest.CreatedAt = _clock.UtcNow.AddDays(-2);
        var middle = NewToken(tenantId, "middle@example.com");
        middle.CreatedAt = _clock.UtcNow.AddDays(-1);
        var newest = NewToken(tenantId, "newest@example.com");
        newest.CreatedAt = _clock.UtcNow;
        var otherTenantToken = NewToken(otherTenantId, "other@example.com");
        await SeedAsync(oldest, middle, newest, otherTenantToken);

        var results = await repo.ListByTenantAsync(tenantId);

        results.Select(i => i.Id).Should().Equal(newest.Id, middle.Id, oldest.Id);
    }

    [Fact]
    public async Task AddAsync_AddsEntityButDoesNotSaveAutomatically()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var token = NewToken(tenantId: Guid.NewGuid(), email: "new@example.com");

        await repo.AddAsync(token);

        using var verificationDb = CreateContext();
        var persisted = await verificationDb.InvitationTokens.FirstOrDefaultAsync(i => i.Id == token.Id);
        persisted.Should().BeNull("AddAsync must stage the entity without calling SaveChangesAsync");

        await db.SaveChangesAsync();

        using var verificationDbAfterSave = CreateContext();
        var persistedAfterSave =
            await verificationDbAfterSave.InvitationTokens.FirstOrDefaultAsync(i => i.Id == token.Id);
        persistedAfterSave.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByTokenHashAsync_LooksUpByTokenHashExactly()
    {
        using var db = CreateContext();
        var repo = new EfInvitationTokenRepository(db);
        var token = NewToken(tenantId: Guid.NewGuid(), email: "hash@example.com");
        token.TokenHash = "expected-hash-value";
        await SeedAsync(token);

        var foundByHash = await repo.GetByTokenHashAsync("expected-hash-value");
        var foundByPlaintextGuess = await repo.GetByTokenHashAsync("plaintext-token-value");

        foundByHash.Should().NotBeNull();
        foundByHash!.Id.Should().Be(token.Id);
        foundByPlaintextGuess.Should().BeNull();
    }

    private InvitationToken NewToken(Guid tenantId, string email)
    {
        return new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            InvitedEmail = email,
            InvitedFullName = "Test Invitee",
            Status = "pending",
            TokenHash = $"hash-{Guid.NewGuid():N}",
            ExpiresAt = _clock.UtcNow.AddDays(7),
            CreatedAt = _clock.UtcNow
        };
    }

    private async Task SeedAsync(params InvitationToken[] tokens)
    {
        using var db = CreateContext();
        db.InvitationTokens.AddRange(tokens);
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
