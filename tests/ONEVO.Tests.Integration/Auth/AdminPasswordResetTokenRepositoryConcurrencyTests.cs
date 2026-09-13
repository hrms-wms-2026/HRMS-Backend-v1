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

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Shared, one-time-per-class setup for AdminPasswordResetTokenRepositoryConcurrencyTests: clones
/// the database ONCE. xUnit's IClassFixture constructs this ONCE and disposes it once after every
/// fact in the class has run, instead of IAsyncLifetime's default of once PER fact - previously
/// this class's own InitializeAsync ran 5 times, once per [Fact]. SeedCredentialAsync creates a
/// dedicated PlatformUser per call (see its own comment): platform_user_credentials has a unique
/// index on (PlatformUserId, CredentialType) filtered WHERE revoked_at IS NULL, so reusing one
/// shared platform user across facts (as this class used to, before this conversion) would violate
/// that index once facts share one database instead of each getting a fresh one.
/// </summary>
public sealed class AdminPasswordResetTokenRepositoryConcurrencyTestsFixture : IAsyncLifetime
{
    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public SystemDateTimeProvider Clock => _clock;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Seeds a dedicated PlatformUser per call: platform_user_credentials has a unique index on
    /// (PlatformUserId, CredentialType) filtered WHERE revoked_at IS NULL, so reusing one shared
    /// platform user across all 5 facts (as InitializeAsync used to) would violate that index once
    /// the facts share one IClassFixture database instead of each getting a fresh one.
    /// </summary>
    public async Task<Guid> SeedCredentialAsync(string tokenHash, DateTimeOffset? expiresAt)
    {
        using var db = CreateContext();
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@test.onevo.dev",
            FullName = "Reset Tester",
            Status = PlatformUser.StatusActive
        };
        db.PlatformUsers.Add(user);
        db.PlatformUserCredentials.Add(new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType,
            PasswordHash = "old-hash",
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm,
            ResetTokenHash = tokenHash,
            ResetTokenExpiresAt = expiresAt,
            CreatedAt = _clock.UtcNow
        });
        await db.SaveChangesAsync();
        return user.Id;
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
/// Proves EfPlatformUserCredentialRepository.TryConsumeResetTokenAsync against real PostgreSQL:
/// the single UPDATE ... WHERE reset_token_expires_at > now guard must let exactly one truly
/// parallel caller win. Mirrors PasswordResetTokenRepositoryConcurrencyTests (tenant). Requires
/// Docker.
/// </summary>
public sealed class AdminPasswordResetTokenRepositoryConcurrencyTests : IClassFixture<AdminPasswordResetTokenRepositoryConcurrencyTestsFixture>
{
    private readonly AdminPasswordResetTokenRepositoryConcurrencyTestsFixture _fixture;

    public AdminPasswordResetTokenRepositoryConcurrencyTests(AdminPasswordResetTokenRepositoryConcurrencyTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ValidToken_ReturnsUserIdAndClearsExpiry()
    {
        var platformUserId = await _fixture.SeedCredentialAsync("hash-valid", expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        using var db = _fixture.CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-valid", _fixture.Clock.UtcNow);

        result.Should().Be(platformUserId);

        using var verifyDb = _fixture.CreateContext();
        var persisted = await verifyDb.PlatformUserCredentials.AsNoTracking()
            .SingleAsync(c => c.PlatformUserId == platformUserId);
        persisted.ResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_AlreadyConsumedToken_ReturnsNull()
    {
        await _fixture.SeedCredentialAsync("hash-used", expiresAt: null);

        using var db = _fixture.CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-used", _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ExpiredToken_ReturnsNull()
    {
        await _fixture.SeedCredentialAsync("hash-expired", expiresAt: _fixture.Clock.UtcNow.AddMinutes(-1));

        using var db = _fixture.CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("hash-expired", _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_UnknownHash_ReturnsNull()
    {
        using var db = _fixture.CreateContext();
        var repo = new EfPlatformUserCredentialRepository(db);

        var result = await repo.TryConsumeResetTokenAsync("no-such-hash", _fixture.Clock.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConsumeResetTokenAsync_ParallelConsume_AllowsExactlyOneWinner()
    {
        const int parallelConsumers = 8;
        var platformUserId = await _fixture.SeedCredentialAsync("hash-parallel", expiresAt: _fixture.Clock.UtcNow.AddHours(1));

        var tasks = Enumerable.Range(0, parallelConsumers).Select(_ => Task.Run(async () =>
        {
            using var attemptDb = _fixture.CreateContext();
            var attemptRepo = new EfPlatformUserCredentialRepository(attemptDb);
            return await attemptRepo.TryConsumeResetTokenAsync("hash-parallel", _fixture.Clock.UtcNow);
        }));
        var results = await Task.WhenAll(tasks);

        results.Count(r => r is not null).Should().Be(
            1, "racing concurrent resets over the same token must never both succeed");
        results.Where(r => r is not null).Should().AllSatisfy(r => r.Should().Be(platformUserId));
    }

}
