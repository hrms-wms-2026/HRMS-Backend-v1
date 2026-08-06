using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.PlatformAccess;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Proves EfPlatformUserCredentialRepository.TryConsumeResetTokenAsync against real PostgreSQL:
/// the single UPDATE ... WHERE reset_token_expires_at > now guard must let exactly one truly
/// parallel caller win. Mirrors PasswordResetTokenRepositoryConcurrencyTests (tenant). Requires
/// Docker.
/// </summary>
public sealed class AdminPasswordResetTokenRepositoryConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_admin_reset_token_concurrency_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _platformUserId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        using var db = CreateContext();
        await db.Database.MigrateAsync();

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "admin-reset-concurrency@test.onevo.dev",
            FullName = "Reset Tester",
            Status = PlatformUser.StatusActive
        };
        db.PlatformUsers.Add(user);
        await db.SaveChangesAsync();
        _platformUserId = user.Id;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task TryConsumeResetTokenAsync_ValidToken_ReturnsUserIdAndClearsExpiry()
    {
        await SeedCredentialAsync("hash-valid", expiresAt: _clock.UtcNow.AddHours(1));

        using var db = CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-valid", _clock.UtcNow);

        result.Should().Be(_platformUserId);

        using var verifyDb = CreateContext();
        var persisted = await verifyDb.PlatformUserCredentials.AsNoTracking()
            .SingleAsync(c => c.PlatformUserId == _platformUserId);
        persisted.ResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_AlreadyConsumedToken_ReturnsNull()
    {
        await SeedCredentialAsync("hash-used", expiresAt: null);

        using var db = CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-used", _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ExpiredToken_ReturnsNull()
    {
        await SeedCredentialAsync("hash-expired", expiresAt: _clock.UtcNow.AddMinutes(-1));

        using var db = CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-expired", _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_UnknownHash_ReturnsNull()
    {
        using var db = CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("no-such-hash", _clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ParallelConsume_AllowsExactlyOneWinner()
    {
        const int parallelConsumers = 8;
        await SeedCredentialAsync("hash-parallel", expiresAt: _clock.UtcNow.AddHours(1));

        var tasks = Enumerable.Range(0, parallelConsumers).Select(_ => Task.Run(async () =>
        {
            using var attemptDb = CreateContext();
            var attemptRepo = new EfPlatformUserCredentialRepository(attemptDb);
            return await attemptRepo.TryConsumeResetTokenAsync("hash-parallel", _clock.UtcNow);
        }));
        var results = await Task.WhenAll(tasks);

        results.Count(r => r is not null).Should().Be(
            1, "racing concurrent resets over the same token must never both succeed");
        results.Where(r => r is not null).Should().AllSatisfy(r => r.Should().Be(_platformUserId));
    }

    private async Task SeedCredentialAsync(string tokenHash, DateTimeOffset? expiresAt)
    {
        using var db = CreateContext();
        db.PlatformUserCredentials.Add(new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = _platformUserId,
            CredentialType = PlatformUserCredential.PasswordType,
            PasswordHash = "old-hash",
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm,
            ResetTokenHash = tokenHash,
            ResetTokenExpiresAt = expiresAt,
            CreatedAt = _clock.UtcNow
        });
        await db.SaveChangesAsync();
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
