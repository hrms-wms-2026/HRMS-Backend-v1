using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Provider-independent behavior tests for EfTenantSessionExchangeChallengeRepository: hash-only
/// persistence, atomic single-use consumption, expiry, and tenant-mismatch rejection. Runs on
/// SQLite (via SqliteTestApplicationDbContext) rather than the EF InMemory provider because
/// TryConsumeAsync uses ExecuteUpdateAsync, which requires a relational provider. Foreign key
/// enforcement is disabled so tests can exercise the table without seeding tenants/users.
///
/// These SQLite tests are not proof of PostgreSQL row-lock/concurrency correctness under true
/// parallel load; MfaChallengeStoreConcurrencyTests (ONEVO.Tests.Integration, real PostgreSQL via
/// Testcontainers) is the authoritative concurrency test for the identical ExecuteUpdateAsync
/// guarded-update pattern used here.
/// </summary>
public sealed class EfTenantSessionExchangeChallengeRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly SecureTokenGenerator _tokens = new();
    private readonly FakeClock _clock = new();

    public EfTenantSessionExchangeChallengeRepositoryTests()
    {
        var databaseName = $"tenant_session_exchange_tests_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";

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
    public async Task AddAsync_PersistsOnlyCodeHash_NeverRawCode()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);
        const string rawCode = "raw-exchange-code";
        var expiresAt = _clock.UtcNow.AddMinutes(2);

        await repo.AddAsync(NewChallenge(rawCode, expiresAt), default);

        using var verificationDb = CreateContext();
        var row = await verificationDb.TenantSessionExchangeChallenges.SingleAsync();
        row.CodeHash.Should().NotBe(rawCode);
        row.CodeHash.Should().Be(_tokens.HashToken(rawCode));
        row.TenantId.Should().Be(TenantId);
        row.UserId.Should().Be(UserId);
        row.ConsumedAt.Should().BeNull();
        row.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task TryConsumeAsync_SucceedsOnce_ThenFails()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);
        const string rawCode = "raw-exchange-code";
        var codeHash = _tokens.HashToken(rawCode);
        await repo.AddAsync(NewChallenge(rawCode, _clock.UtcNow.AddMinutes(2)), default);

        var first = await repo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow, default);
        var second = await repo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow, default);

        first.Should().NotBeNull();
        first!.UserId.Should().Be(UserId);
        first.TenantId.Should().Be(TenantId);
        first.AuthOrigin.Should().Be("password");
        second.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeAsync_ConcurrentConsumeFromAnotherContext_OnlyOneSucceeds()
    {
        using var creatorDb = CreateContext();
        var creatorRepo = new EfTenantSessionExchangeChallengeRepository(creatorDb);
        const string rawCode = "raw-exchange-code";
        var codeHash = _tokens.HashToken(rawCode);
        await creatorRepo.AddAsync(NewChallenge(rawCode, _clock.UtcNow.AddMinutes(2)), default);

        using var firstDb = CreateContext();
        using var secondDb = CreateContext();
        var firstRepo = new EfTenantSessionExchangeChallengeRepository(firstDb);
        var secondRepo = new EfTenantSessionExchangeChallengeRepository(secondDb);

        var firstConsume = await firstRepo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow, default);
        var secondConsume = await secondRepo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow, default);

        firstConsume.Should().NotBeNull();
        secondConsume.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredCode_Fails()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);
        const string rawCode = "raw-exchange-code";
        var codeHash = _tokens.HashToken(rawCode);
        await repo.AddAsync(NewChallenge(rawCode, _clock.UtcNow.AddMinutes(2)), default);

        var consumed = await repo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow.AddMinutes(3), default);

        consumed.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeAsync_WrongTenant_Fails_AndDoesNotConsumeForCorrectTenant()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);
        const string rawCode = "raw-exchange-code";
        var codeHash = _tokens.HashToken(rawCode);
        await repo.AddAsync(NewChallenge(rawCode, _clock.UtcNow.AddMinutes(2)), default);

        var wrongTenantAttempt = await repo.TryConsumeAsync(codeHash, OtherTenantId, _clock.UtcNow, default);
        var correctTenantAttempt = await repo.TryConsumeAsync(codeHash, TenantId, _clock.UtcNow, default);

        wrongTenantAttempt.Should().BeNull();
        correctTenantAttempt.Should().NotBeNull("a failed wrong-tenant attempt must not burn the single use");
    }

    [Fact]
    public async Task TryConsumeAsync_UnknownCode_ReturnsNull()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);

        var consumed = await repo.TryConsumeAsync("not-a-real-hash", TenantId, _clock.UtcNow, default);

        consumed.Should().BeNull();
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesExpiredAndConsumedRows_KeepsActiveRows()
    {
        using var db = CreateContext();
        var repo = new EfTenantSessionExchangeChallengeRepository(db);
        await repo.AddAsync(NewChallenge("expired", _clock.UtcNow.AddMinutes(-1)), default);
        var consumedHash = _tokens.HashToken("consumed");
        await repo.AddAsync(NewChallenge("consumed", _clock.UtcNow.AddMinutes(2)), default);
        await repo.TryConsumeAsync(consumedHash, TenantId, _clock.UtcNow, default);
        await repo.AddAsync(NewChallenge("active", _clock.UtcNow.AddMinutes(2)), default);

        var removed = await repo.CleanupExpiredAsync(_clock.UtcNow, default);

        removed.Should().Be(2);
        using var verificationDb = CreateContext();
        var remaining = await verificationDb.TenantSessionExchangeChallenges.AsNoTracking().ToListAsync();
        remaining.Should().ContainSingle(c => c.CodeHash == _tokens.HashToken("active"));
    }

    private TenantSessionExchangeChallenge NewChallenge(string rawCode, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        CodeHash = _tokens.HashToken(rawCode),
        TenantId = TenantId,
        UserId = UserId,
        AuthOrigin = "password",
        ExpiresAt = expiresAt,
        ConsumedAt = null,
        CreatedAt = _clock.UtcNow,
        UpdatedAt = _clock.UtcNow
    };

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
        private DateTimeOffset _utcNow = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
