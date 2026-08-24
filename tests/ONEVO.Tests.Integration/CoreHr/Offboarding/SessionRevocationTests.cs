using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.Offboarding;

/// <summary>
/// Exercises ISessionRepository.RevokeAllActiveByUserIdAsync against real PostgreSQL because
/// ExecuteUpdateAsync is not supported by EF InMemory.
/// </summary>
public sealed class SessionRevocationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_session_revocation_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task RevokeAllActiveByUserIdAsync_RevokesOnlyThatUsersActiveSessions()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        db.Sessions.AddRange(
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsRevoked = false, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), KeyHash = Guid.NewGuid().ToString("N") },
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsRevoked = true, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), KeyHash = Guid.NewGuid().ToString("N") },
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = otherUserId, IsRevoked = false, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), KeyHash = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();

        ISessionRepository repo = new EfAuthRepository(db);
        var count = await repo.RevokeAllActiveByUserIdAsync(userId);

        count.Should().Be(1);
        (await db.Sessions.Where(s => s.UserId == userId).AllAsync(s => s.IsRevoked)).Should().BeTrue();
        (await db.Sessions.Where(s => s.UserId == otherUserId).AnyAsync(s => !s.IsRevoked)).Should().BeTrue();
    }

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }
}
