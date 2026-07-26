using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Integrations;

public sealed class UserIntegrationConnectionPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_user_integrations_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await AdminTestFactory.MigrateDatabaseAsync(_postgres.GetConnectionString());
        _factory = new AdminTestFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Upsert_encrypts_before_real_ef_persistence_and_unique_index_is_enforced()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET session_replication_role = replica; " +
            "SELECT set_config('app.tenant_context_mode', 'admin', false);");

        var handler = new UpsertOwnUserIntegrationConnectionCommandHandler(
            new FixedCurrentUser(tenantId, userId),
            new EfUserIntegrationConnectionRepository(db),
            new PrefixEncryptionService());
        var result = await handler.Handle(
            new UpsertOwnUserIntegrationConnectionCommand(
                "github",
                "provider-user",
                "octocat",
                null,
                "test-access-value",
                "test-refresh-value",
                DateTimeOffset.UtcNow.AddHours(1),
                ["repo"]),
            default);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.UserIntegrationConnections
            .IgnoreQueryFilters()
            .SingleAsync(connection =>
                connection.TenantId == tenantId &&
                connection.UserId == userId);
        Assert.Equal("cipher:test-access-value", stored.AccessTokenEncrypted);
        Assert.Equal("cipher:test-refresh-value", stored.RefreshTokenEncrypted);
        Assert.DoesNotContain("AccessToken", result.Value!.GetType().GetProperties()
            .Select(property => property.Name));

        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.UserIntegrationConnections.Add(new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                IntegrationKey = "github",
                Status = "connected",
                ConnectedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Migration_enforces_status_constraint_and_tenant_rls()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await ExecuteAsync(connection, "SET session_replication_role = replica");
        await ExecuteAsync(connection, "SELECT set_config('app.tenant_context_mode', 'admin', false)");
        await InsertConnectionAsync(connection, tenantA, "connected");
        await InsertConnectionAsync(connection, tenantB, "connected");

        var invalid = await Assert.ThrowsAsync<PostgresException>(
            () => InsertConnectionAsync(connection, Guid.NewGuid(), "invalid"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);

        await ExecuteAsync(connection, "SET session_replication_role = origin");
        await ExecuteAsync(connection, "CREATE ROLE onevo_tenant_runtime NOLOGIN");
        await ExecuteAsync(
            connection,
            "GRANT SELECT, INSERT, UPDATE, DELETE ON user_integration_connections TO onevo_tenant_runtime");
        await ExecuteAsync(connection, "SET ROLE onevo_tenant_runtime");
        await ExecuteAsync(connection, "SELECT set_config('app.tenant_context_mode', 'tenant', false)");
        await ExecuteAsync(
            connection,
            $"SELECT set_config('app.current_tenant_id', '{tenantA}', false)");

        await using var countCommand = new NpgsqlCommand(
            "SELECT count(*) FROM user_integration_connections",
            connection);
        var visibleCount = (long)(await countCommand.ExecuteScalarAsync())!;
        Assert.Equal(1, visibleCount);

        await ExecuteAsync(connection, "RESET ROLE");
    }

    private static async Task InsertConnectionAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        string status)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_integration_connections
                (id, tenant_id, user_id, integration_key, status, connected_at, created_at)
            VALUES
                (@id, @tenant_id, @user_id, 'github', @status, now(), now())
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public FixedCurrentUser(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "integration-test@example.test";
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => false;
    }

    private sealed class PrefixEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => $"cipher:{plainText}";
        public string Decrypt(string cipherText) => cipherText[7..];
        public byte[] EncryptBytes(string plainText) =>
            System.Text.Encoding.UTF8.GetBytes(Encrypt(plainText));
        public string DecryptBytes(byte[] cipherBytes) =>
            Decrypt(System.Text.Encoding.UTF8.GetString(cipherBytes));
    }
}
