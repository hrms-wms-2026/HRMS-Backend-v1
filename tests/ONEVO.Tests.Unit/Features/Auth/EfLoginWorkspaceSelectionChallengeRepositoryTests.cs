using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Mirrors PostgresMfaChallengeStoreTests's SQLite shared in-memory harness: provider-independent
/// behavior only (hash-only persistence, expiry). The atomic-update SQL itself is proven for real
/// against PostgreSQL in Task 16's integration tests, matching how PostgresMfaChallengeStore's
/// equivalent SQL is only proven via MfaChallengeStoreConcurrencyTests in Integration.
/// </summary>
public sealed class EfLoginWorkspaceSelectionChallengeRepositoryTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly ApplicationDbContext _schemaContext;
    private readonly SecureTokenGenerator _tokens = new();
    private readonly FakeClock _clock = new();

    public EfLoginWorkspaceSelectionChallengeRepositoryTests()
    {
        var databaseName = $"login_workspace_selection_challenge_tests_{Guid.NewGuid():N}";
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
    public async Task CreateAsync_StoresHashOnly_NotRawChallenge()
    {
        using var db = CreateContext();
        var repository = CreateRepository(db);
        var candidates = new[]
        {
            new WorkspaceCandidateSnapshot(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test")
        };

        var rawChallenge = await repository.CreateAsync(
            "user@example.com", candidates, "127.0.0.1", "test-agent", TimeSpan.FromMinutes(5));

        using var verificationDb = CreateContext();
        var storedRow = await verificationDb.LoginWorkspaceSelectionChallenges.SingleAsync();
        storedRow.ChallengeHash.Should().NotBe(rawChallenge);
        storedRow.ChallengeHash.Should().Be(_tokens.HashToken(rawChallenge));
        storedRow.CandidateWorkspacesJson.Should().Contain("\"Origin\":\"password\"");
        storedRow.CandidateWorkspacesJson.Should().NotContain("password_hash");
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsNull_ForExpiredChallenge()
    {
        using var db = CreateContext();
        var repository = CreateRepository(db);
        var candidates = new[]
        {
            new WorkspaceCandidateSnapshot(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test")
        };

        var rawChallenge = await repository.CreateAsync(
            "user@example.com", candidates, null, null, TimeSpan.FromMinutes(-1));

        using var lookupDb = CreateContext();
        var state = await CreateRepository(lookupDb).GetActiveAsync(rawChallenge);

        state.Should().BeNull();
    }

    private EfLoginWorkspaceSelectionChallengeRepository CreateRepository(ApplicationDbContext db)
    {
        return new EfLoginWorkspaceSelectionChallengeRepository(db, _tokens, _clock);
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
        private DateTimeOffset _utcNow = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;
        public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
