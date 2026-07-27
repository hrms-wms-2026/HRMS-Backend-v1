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
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Proves EfAuthRepository.TryConsumeResetTokenAsync against a real PostgreSQL server: the single
/// UPDATE ... WHERE used_at IS NULL guard must let exactly one truly parallel caller win, and must
/// correctly reject used/expired/wrong-tenant/unknown tokens. A prior SQLite-backed attempt at these
/// same assertions failed - Microsoft.Data.Sqlite binds a raw-SQL-interpolated DateTimeOffset
/// parameter differently than EF's SQLite column converter formats the stored value, so an
/// "expires_at &gt; @now" raw SQL comparison silently matched zero rows there even though the LINQ
/// equivalent worked. That is a SQLite ADO parameter-binding quirk, not a defect in the production
/// code path; Npgsql has no such mismatch for timestamptz, so this suite is the actual proof for the
/// real target database. Requires Docker.
/// </summary>
public sealed class PasswordResetTokenRepositoryConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_reset_token_concurrency_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        using var db = CreateContext();
        await db.Database.MigrateAsync();

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
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ValidToken_ReturnsUserIdAndMarksUsed()
    {
        var tokenId = await SeedTokenAsync("hash-valid", usedAt: null, expiresAt: _clock.UtcNow.AddHours(1));

        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-valid", _tenantId, _clock.UtcNow);

        result.Should().Be(_userId);

        using var verifyDb = CreateContext();
        var persisted = await verifyDb.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        persisted.UsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_AlreadyUsedToken_ReturnsNull()
    {
        await SeedTokenAsync("hash-used", usedAt: _clock.UtcNow.AddMinutes(-1), expiresAt: _clock.UtcNow.AddHours(1));

        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-used", _tenantId, _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ExpiredToken_ReturnsNull()
    {
        await SeedTokenAsync("hash-expired", usedAt: null, expiresAt: _clock.UtcNow.AddMinutes(-1));

        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-expired", _tenantId, _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_WrongTenant_ReturnsNull()
    {
        await SeedTokenAsync("hash-wrong-tenant", usedAt: null, expiresAt: _clock.UtcNow.AddHours(1));

        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-wrong-tenant", Guid.NewGuid(), _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_UnknownHash_ReturnsNull()
    {
        using var db = CreateContext();
        var repo = new EfAuthRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("no-such-hash", _tenantId, _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ParallelConsume_AllowsExactlyOneWinner()
    {
        const int parallelConsumers = 8;
        var tokenId = await SeedTokenAsync("hash-parallel", usedAt: null, expiresAt: _clock.UtcNow.AddHours(1));

        var consumeTasks = new List<Task<Guid?>>();
        for (var i = 0; i < parallelConsumers; i++)
        {
            consumeTasks.Add(Task.Run(async () =>
            {
                using var attemptDb = CreateContext();
                var attemptRepo = new EfAuthRepository(attemptDb);
                return await attemptRepo.TryConsumeResetTokenAsync("hash-parallel", _tenantId, _clock.UtcNow);
            }));
        }
        var results = await Task.WhenAll(consumeTasks);

        results.Count(r => r is not null).Should().Be(
            1, "racing concurrent resets over the same token must never both succeed");
        results.Where(r => r is not null).Should().AllSatisfy(r => r.Should().Be(_userId));

        using var verifyDb = CreateContext();
        var persisted = await verifyDb.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.Id == tokenId);
        persisted.UsedAt.Should().NotBeNull();
    }

    private async Task<Guid> SeedTokenAsync(string tokenHash, DateTimeOffset? usedAt, DateTimeOffset expiresAt)
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

    private ApplicationDbContext CreateContext()
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
