using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// Proves the atomicity of PostgresMfaChallengeStore.RegisterFailedAttemptAsync against a real
/// PostgreSQL server: truly parallel invalid MFA attempts on separate connections must not lose
/// increments, and the lockout threshold must be enforced by the single atomic UPDATE.
/// Requires Docker (Testcontainers).
/// </summary>
public sealed class MfaChallengeStoreConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_mfa_challenge_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SecureTokenGenerator _tokens = new();
    private readonly SystemDateTimeProvider _clock = new();

    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        using var db = CreateContext();
        await db.Database.MigrateAsync();

        // mfa_challenges has enforced foreign keys to tenants and users, so seed one of each.
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "MFA Concurrency Test Tenant",
            Slug = "mfa-concurrency-test",
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = "mfa-concurrency@test.onevo.dev",
            PasswordHash = "not-a-real-hash",
            FirstName = "Mfa",
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
    public async Task ParallelInvalidAttempts_DoNotLoseIncrements()
    {
        const int parallelAttempts = 8;
        const int maximumAttempts = 100;

        string rawChallenge;
        using (var creatorDb = CreateContext())
        {
            var creatorStore = CreateStore(creatorDb);
            rawChallenge = await creatorStore.CreateAsync(_userId, _tenantId, TimeSpan.FromMinutes(10));
        }

        var attemptTasks = new List<Task<bool>>();
        for (var i = 0; i < parallelAttempts; i++)
        {
            attemptTasks.Add(Task.Run(async () =>
            {
                using var attemptDb = CreateContext();
                var attemptStore = CreateStore(attemptDb);
                return await attemptStore.RegisterFailedAttemptAsync(rawChallenge, maximumAttempts);
            }));
        }
        var results = await Task.WhenAll(attemptTasks);

        results.Should().AllSatisfy(r => r.Should().BeTrue());

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges
            .AsNoTracking()
            .SingleAsync(c => c.ChallengeHash == _tokens.HashToken(rawChallenge));
        row.FailedAttemptCount.Should().Be(
            parallelAttempts,
            "every parallel failed attempt must add exactly one; no increment may be lost");
        row.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task ParallelInvalidAttempts_CannotLeaveChallengeActivePastMaximum()
    {
        const int parallelAttempts = 8;
        const int maximumAttempts = 3;

        string rawChallenge;
        using (var creatorDb = CreateContext())
        {
            var creatorStore = CreateStore(creatorDb);
            rawChallenge = await creatorStore.CreateAsync(_userId, _tenantId, TimeSpan.FromMinutes(10));
        }

        var attemptTasks = new List<Task<bool>>();
        for (var i = 0; i < parallelAttempts; i++)
        {
            attemptTasks.Add(Task.Run(async () =>
            {
                using var attemptDb = CreateContext();
                var attemptStore = CreateStore(attemptDb);
                return await attemptStore.RegisterFailedAttemptAsync(rawChallenge, maximumAttempts);
            }));
        }
        var results = await Task.WhenAll(attemptTasks);

        // Once consumed_at is set by the increment that reaches the maximum, later UPDATEs match
        // zero rows, so the counter freezes exactly at the maximum.
        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges
            .AsNoTracking()
            .SingleAsync(c => c.ChallengeHash == _tokens.HashToken(rawChallenge));
        row.FailedAttemptCount.Should().Be(maximumAttempts);
        row.ConsumedAt.Should().NotBeNull("reaching the maximum must invalidate the challenge");

        var acceptedCount = results.Count(r => r);
        acceptedCount.Should().BeLessThan(
            maximumAttempts,
            "only attempts that leave the challenge active may return true");

        using var readerDb = CreateContext();
        var readerStore = CreateStore(readerDb);
        var stateAfterLockout = await readerStore.GetAsync(rawChallenge);
        var consumeAfterLockout = await readerStore.TryConsumeAsync(rawChallenge);
        stateAfterLockout.Should().BeNull();
        consumeAfterLockout.Should().BeNull();
    }

    [Fact]
    public async Task ParallelConsume_AllowsExactlyOneWinner()
    {
        const int parallelConsumers = 8;

        string rawChallenge;
        using (var creatorDb = CreateContext())
        {
            var creatorStore = CreateStore(creatorDb);
            rawChallenge = await creatorStore.CreateAsync(_userId, _tenantId, TimeSpan.FromMinutes(10));
        }

        var consumeTasks = new List<Task<ONEVO.Application.Features.Auth.Login.ServiceInterfaces.MfaChallengeState?>>();
        for (var i = 0; i < parallelConsumers; i++)
        {
            consumeTasks.Add(Task.Run(async () =>
            {
                using var consumerDb = CreateContext();
                var consumerStore = CreateStore(consumerDb);
                return await consumerStore.TryConsumeAsync(rawChallenge);
            }));
        }
        var results = await Task.WhenAll(consumeTasks);

        results.Count(r => r is not null).Should().Be(
            1,
            "racing successful verifies must never consume the same challenge twice");

        using var verificationDb = CreateContext();
        var row = await verificationDb.MfaChallenges
            .AsNoTracking()
            .SingleAsync(c => c.ChallengeHash == _tokens.HashToken(rawChallenge));
        row.ConsumedAt.Should().NotBeNull();
        row.FailedAttemptCount.Should().Be(0);

        using var readerDb = CreateContext();
        var readerStore = CreateStore(readerDb);
        (await readerStore.GetAsync(rawChallenge)).Should().BeNull();
        (await readerStore.TryConsumeAsync(rawChallenge)).Should().BeNull();
    }

    private PostgresMfaChallengeStore CreateStore(ApplicationDbContext db)
    {
        return new PostgresMfaChallengeStore(db, _tokens, _clock);
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
