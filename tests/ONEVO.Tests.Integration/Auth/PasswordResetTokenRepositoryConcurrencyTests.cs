using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Shared, one-time-per-class setup for PasswordResetTokenRepositoryConcurrencyTests: clones the
/// database and seeds the tenant/user ONCE. xUnit's IClassFixture constructs this ONCE and
/// disposes it once after every fact in the class has run, instead of IAsyncLifetime's default of
/// once PER fact - previously this class's own InitializeAsync ran 6 times, once per [Fact]. Every
/// fact seeds its own token under a distinct hash ("hash-valid", "hash-used", "hash-expired",
/// "hash-wrong-tenant", "hash-parallel"), so there is no cross-fact state-sharing risk from
/// converting this class; the parallel-consume fact's correctness comes from real Postgres
/// row-locking on that one token, independent of database isolation between facts.
/// </summary>
public sealed class PasswordResetTokenRepositoryConcurrencyTestsFixture : IAsyncLifetime
{
    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _userId;

    public SystemDateTimeProvider Clock => _clock;
    public Guid TenantId => _tenantId;
    public Guid UserId => _userId;


    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();

        using var db = CreateContext();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Reset Token Concurrency Test Tenant",
            Slug = "reset-token-concurrency-test",
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "reset-token-concurrency@test.onevo.dev",
            PasswordHash = "not-a-real-hash",
            FirstName = "Reset",
            LastName = "Tester",
            IsActive = true
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        _tenantId = tenant.Id;
        _userId = user.Id;
    }

    public async Task DisposeAsync()
    {
    }

    public async Task<Guid> SeedTokenAsync(string tokenHash, DateTimeOffset? usedAt, DateTimeOffset expiresAt)
    {
        using var db = CreateContext();
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            UserId = _userId,
            TokenHash = tokenHash,
            UsedAt = usedAt,
            ExpiresAt = expiresAt,
            CreatedAt = _clock.UtcNow
        };
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync();
        return token.Id;
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

}

/// <summary>
/// Proves EfPasswordResetTokenRepository.TryConsumeResetTokenAsync against a real PostgreSQL server: the single
/// UPDATE ... WHERE used_at IS NULL guard must let exactly one truly parallel caller win, and must
/// correctly reject used/expired/wrong-tenant/unknown tokens. A prior SQLite-backed attempt at these
/// same assertions failed - Microsoft.Data.Sqlite binds a raw-SQL-interpolated DateTimeOffset
/// parameter differently than EF's SQLite column converter formats the stored value, so an
/// "expires_at &gt; @now" raw SQL comparison silently matched zero rows there even though the LINQ
/// equivalent worked. That is a SQLite ADO parameter-binding quirk, not a defect in the production
/// code path; Npgsql has no such mismatch for timestamptz, so this suite is the actual proof for the
/// real target database. Requires Docker.
/// </summary>
public sealed class PasswordResetTokenRepositoryConcurrencyTests : IClassFixture<PasswordResetTokenRepositoryConcurrencyTestsFixture>
{
    private readonly PasswordResetTokenRepositoryConcurrencyTestsFixture _fixture;

    public PasswordResetTokenRepositoryConcurrencyTests(PasswordResetTokenRepositoryConcurrencyTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ValidToken_ReturnsUserIdAndMarksUsed()
    {
        var tokenId = await _fixture.SeedTokenAsync("hash-valid", usedAt: null, expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        using var db = _fixture.CreateContext();
        var repo = new EfPasswordResetTokenRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-valid", _fixture.TenantId, _fixture.Clock.UtcNow);

        result.Should().Be(_fixture.UserId);

        using var verifyDb = _fixture.CreateContext();
        var persisted = await verifyDb.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        persisted.UsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_AlreadyUsedToken_ReturnsNull()
    {
        await _fixture.SeedTokenAsync("hash-used", usedAt: _fixture.Clock.UtcNow.AddMinutes(-1), expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        using var db = _fixture.CreateContext();
        var repo = new EfPasswordResetTokenRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-used", _fixture.TenantId, _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ExpiredToken_ReturnsNull()
    {
        await _fixture.SeedTokenAsync("hash-expired", usedAt: null, expiresAt: _fixture.Clock.UtcNow.AddMinutes(-1));

        using var db = _fixture.CreateContext();
        var repo = new EfPasswordResetTokenRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-expired", _fixture.TenantId, _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_WrongTenant_ReturnsNull()
    {
        await _fixture.SeedTokenAsync("hash-wrong-tenant", usedAt: null, expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        using var db = _fixture.CreateContext();
        var repo = new EfPasswordResetTokenRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-wrong-tenant", Guid.NewGuid(), _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_UnknownHash_ReturnsNull()
    {
        using var db = _fixture.CreateContext();
        var repo = new EfPasswordResetTokenRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("no-such-hash", _fixture.TenantId, _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ParallelConsume_AllowsExactlyOneWinner()
    {
        const int parallelConsumers = 8;
        var tokenId = await _fixture.SeedTokenAsync("hash-parallel", usedAt: null, expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        var consumeTasks = new List<Task<Guid?>>();
        for (var i = 0; i < parallelConsumers; i++)
        {
            consumeTasks.Add(Task.Run(async () =>
            {
                using var attemptDb = _fixture.CreateContext();
                var attemptRepo = new EfPasswordResetTokenRepository(attemptDb);
                return await attemptRepo.TryConsumeResetTokenAsync("hash-parallel", _fixture.TenantId, _fixture.Clock.UtcNow);
            }));
        }
        var results = await Task.WhenAll(consumeTasks);

        results.Count(r => r is not null).Should().Be(
            1, "racing concurrent resets over the same token must never both succeed");
        results.Where(r => r is not null).Should().AllSatisfy(r => r.Should().Be(_fixture.UserId));

        using var verifyDb = _fixture.CreateContext();
        var persisted = await verifyDb.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        persisted.UsedAt.Should().NotBeNull();
    }

}
