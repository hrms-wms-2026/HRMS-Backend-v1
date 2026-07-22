using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for PostgresMfaChallengeStore: hash-only persistence,
/// expiry, single-use consumption, invalid challenges, and max-failed-attempt lockout in
/// single-threaded flows. Runs on SQLite shared in-memory (via the test-only
/// SqliteTestApplicationDbContext) rather than the EF InMemory provider because
/// RegisterFailedAttemptAsync uses ExecuteUpdateAsync, which requires a relational provider.
/// Foreign key enforcement is disabled so the tests can exercise mfa_challenges without seeding
/// tenants/users.
///
/// These SQLite tests are NOT proof of PostgreSQL row-lock/concurrency correctness. The
/// authoritative concurrency verification (truly parallel writers, lost-increment protection,
/// single-winner consume) is MfaChallengeStoreConcurrencyTests in ONEVO.Tests.Integration,
/// which runs against real PostgreSQL via Testcontainers.
/// </summary>
public sealed class PostgresMfaChallengeStoreTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const int MaximumAttempts = 5;

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly SecureTokenGenerator _tokens = new();
    private readonly FakeClock _clock = new();

    public PostgresMfaChallengeStoreTests()
    {
        var databaseName = $"mfa_store_tests_{Guid.NewGuid():N}";
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
    public async Task CreateAsync_PersistsOnlyChallengeHash_NeverRawChallenge()
    {
        using var db = CreateContext();
        var store = CreateStore(db);

        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges.SingleAsync();
        row.ChallengeHash.Should().NotBe(rawChallenge);
        row.ChallengeHash.Should().Be(_tokens.HashToken(rawChallenge));
        row.TenantId.Should().Be(TenantId);
        row.UserId.Should().Be(UserId);
        row.FailedAttemptCount.Should().Be(0);
        row.ConsumedAt.Should().BeNull();
        row.ExpiresAt.Should().Be(_clock.UtcNow.Add(Lifetime));
    }

    [Fact]
    public async Task GetAsync_ActiveChallenge_ReturnsState()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        var state = await store.GetAsync(rawChallenge);

        state.Should().NotBeNull();
        state!.UserId.Should().Be(UserId);
        state.TenantId.Should().Be(TenantId);
        state.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_UnknownChallenge_ReturnsNull()
    {
        using var db = CreateContext();
        var store = CreateStore(db);

        var state = await store.GetAsync("not-a-real-challenge");

        state.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExpiredChallenge_ReturnsNull()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        _clock.Advance(Lifetime + TimeSpan.FromSeconds(1));

        var state = await store.GetAsync(rawChallenge);

        state.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeAsync_SucceedsOnce_ThenFails()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        var firstAttempt = await store.TryConsumeAsync(rawChallenge);
        var secondAttempt = await store.TryConsumeAsync(rawChallenge);

        firstAttempt.Should().NotBeNull();
        firstAttempt!.UserId.Should().Be(UserId);
        secondAttempt.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeAsync_ConcurrentConsumeFromAnotherContext_OnlyOneSucceeds()
    {
        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        var firstStore = CreateStore(firstDb);
        var secondStore = CreateStore(secondDb);
        var rawChallenge = await firstStore.CreateAsync(UserId, TenantId, Lifetime);

        // Both stores load the active row before either persists consumption, simulating two
        // racing verify requests on different DB connections.
        var firstState = await firstStore.GetAsync(rawChallenge);
        var secondState = await secondStore.GetAsync(rawChallenge);
        firstState.Should().NotBeNull();
        secondState.Should().NotBeNull();

        var firstConsume = await firstStore.TryConsumeAsync(rawChallenge);
        var secondConsume = await secondStore.TryConsumeAsync(rawChallenge);

        firstConsume.Should().NotBeNull();
        secondConsume.Should().BeNull();
    }

    [Fact]
    public async Task ConsumedChallenge_CannotBeReadOrReused()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        await store.TryConsumeAsync(rawChallenge);

        var state = await store.GetAsync(rawChallenge);
        var failedAttemptAccepted = await store.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        state.Should().BeNull();
        failedAttemptAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_IncrementsCount()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        var accepted = await store.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        accepted.Should().BeTrue();
        var state = await store.GetAsync(rawChallenge);
        state.Should().NotBeNull();
        state!.FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_MaximumReached_InvalidatesChallenge()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        for (var attempt = 1; attempt < MaximumAttempts; attempt++)
        {
            var accepted = await store.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);
            accepted.Should().BeTrue();
        }

        var finalAttempt = await store.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        finalAttempt.Should().BeFalse();
        var state = await store.GetAsync(rawChallenge);
        state.Should().BeNull();
        var consumeAfterLockout = await store.TryConsumeAsync(rawChallenge);
        consumeAfterLockout.Should().BeNull();
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_ExpiredChallenge_ReturnsFalse()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        _clock.Advance(Lifetime + TimeSpan.FromSeconds(1));

        var accepted = await store.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_StaleReadersOnSeparateContexts_DoNotLoseIncrements()
    {
        // Regression for the lost-update bug: two requests read the same failed_attempt_count
        // before either writes. The old load-increment-save implementation persisted 1 here; the
        // atomic UPDATE must persist 2.
        using var creatorDb = CreateContext();
        var creatorStore = CreateStore(creatorDb);
        var rawChallenge = await creatorStore.CreateAsync(UserId, TenantId, Lifetime);

        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        var firstStore = CreateStore(firstDb);
        var secondStore = CreateStore(secondDb);

        // Both contexts load the row (count = 0) before either registers a failure.
        var firstPreload = await firstStore.GetAsync(rawChallenge);
        var secondPreload = await secondStore.GetAsync(rawChallenge);
        firstPreload!.FailedAttempts.Should().Be(0);
        secondPreload!.FailedAttempts.Should().Be(0);

        var firstAccepted = await firstStore.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);
        var secondAccepted = await secondStore.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        firstAccepted.Should().BeTrue();
        secondAccepted.Should().BeTrue();

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges.AsNoTracking().SingleAsync();
        row.FailedAttemptCount.Should().Be(2, "each failed attempt must add exactly one, with no lost increments");
        row.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_StaleReadersReachingMax_ChallengeCannotStayActive()
    {
        // Two stale readers race toward a maximum of 2: whichever increment lands second must
        // trip the lockout inside the same atomic UPDATE, so the challenge can never remain
        // active past the threshold.
        const int maximumAttempts = 2;
        using var creatorDb = CreateContext();
        var creatorStore = CreateStore(creatorDb);
        var rawChallenge = await creatorStore.CreateAsync(UserId, TenantId, Lifetime);

        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        var firstStore = CreateStore(firstDb);
        var secondStore = CreateStore(secondDb);
        await firstStore.GetAsync(rawChallenge);
        await secondStore.GetAsync(rawChallenge);

        var firstAccepted = await firstStore.RegisterFailedAttemptAsync(rawChallenge, maximumAttempts);
        var secondAccepted = await secondStore.RegisterFailedAttemptAsync(rawChallenge, maximumAttempts);

        firstAccepted.Should().BeTrue();
        secondAccepted.Should().BeFalse("the second increment reaches the maximum and must lock the challenge");

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges.AsNoTracking().SingleAsync();
        row.FailedAttemptCount.Should().Be(maximumAttempts);
        row.ConsumedAt.Should().NotBeNull("reaching the maximum must invalidate the challenge");

        using var readerDb = CreateContext();
        var readerStore = CreateStore(readerDb);
        var stateAfterLockout = await readerStore.GetAsync(rawChallenge);
        var consumeAfterLockout = await readerStore.TryConsumeAsync(rawChallenge);
        stateAfterLockout.Should().BeNull();
        consumeAfterLockout.Should().BeNull();
    }

    [Fact]
    public async Task RegisterFailedAttemptAsync_AfterConcurrentConsume_ReturnsFalse()
    {
        using var attackerDb = CreateContext();
        using var ownerDb = CreateContext();
        var attackerStore = CreateStore(attackerDb);
        var ownerStore = CreateStore(ownerDb);
        var rawChallenge = await ownerStore.CreateAsync(UserId, TenantId, Lifetime);

        // The attacker's context loads the row while it is still active.
        await attackerStore.GetAsync(rawChallenge);

        // The owner consumes the challenge first; the late failed attempt must not resurrect it.
        var consumed = await ownerStore.TryConsumeAsync(rawChallenge);
        consumed.Should().NotBeNull();

        var accepted = await attackerStore.RegisterFailedAttemptAsync(rawChallenge, MaximumAttempts);

        accepted.Should().BeFalse();
        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges.AsNoTracking().SingleAsync();
        row.FailedAttemptCount.Should().Be(0, "a consumed challenge must not accept further increments");
    }

    [Fact]
    public async Task CreateAsync_RemovesExpiredAndConsumedRowsForUser()
    {
        using var db = CreateContext();
        var store = CreateStore(db);
        var consumedChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);
        await store.TryConsumeAsync(consumedChallenge);
        var expiredChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        _clock.Advance(Lifetime + TimeSpan.FromSeconds(1));
        var activeChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        using var verificationDb = CreateContext();
        var rows = await verificationDb.MfaChallenges.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].ChallengeHash.Should().Be(_tokens.HashToken(activeChallenge));
        rows.Should().NotContain(r => r.ChallengeHash == _tokens.HashToken(consumedChallenge));
        rows.Should().NotContain(r => r.ChallengeHash == _tokens.HashToken(expiredChallenge));
    }

    [Fact]
    public async Task RawChallengeValue_NeverAppearsInAnyPersistedColumn()
    {
        using var db = CreateContext();
        var store = CreateStore(db);

        var rawChallenge = await store.CreateAsync(UserId, TenantId, Lifetime);

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges.SingleAsync();
        var persistedStrings = new[] { row.ChallengeHash };
        persistedStrings.Should().NotContain(rawChallenge);
    }

    private PostgresMfaChallengeStore CreateStore(ApplicationDbContext db)
    {
        return new PostgresMfaChallengeStore(db, _tokens, _clock);
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
        private DateTimeOffset _utcNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
