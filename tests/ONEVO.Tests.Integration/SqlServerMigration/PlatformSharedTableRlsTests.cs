using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.SqlServer;

/// <summary>
/// outbox_messages and billing_audit_logs allow a null tenant_id and are written by anonymous paths,
/// so they get their own rule: system mode may use them, tenants see only their own rows, and a
/// tenant may add an unowned row but never read it back.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PlatformSharedTableRlsTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = await ConnectAsync(_database.Admin, "admin", null);
        await RowFactory.DisableConstraintsAsync(_admin);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private static async Task<SqlConnection> ConnectAsync(string connectionString, string mode, Guid? tenant)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, tenant?.ToString() ?? "", mode);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<int> CountAsync(SqlConnection connection, string table, string where)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE {where}";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("billing_audit_logs")]
    public async Task Tenant_CanAddAnUnownedRow_ButCannotReadIt(string table)
    {
        var id = Guid.NewGuid();
        await using var tenant = await ConnectAsync(_database.App, "tenant", Guid.NewGuid());

        await RowFactory.InsertAsync(tenant, table, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = null,
        });

        Assert.Equal(0, await CountAsync(tenant, table, $"id = '{id}'"));
        Assert.Equal(1, await CountAsync(_admin, table, $"id = '{id}'"));
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("billing_audit_logs")]
    public async Task SystemMode_CanWriteAndReadForAnyTenant(string table)
    {
        var (id, owner) = (Guid.NewGuid(), Guid.NewGuid());
        await using var system = await ConnectAsync(_database.App, "system", null);

        await RowFactory.InsertAsync(system, table, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = owner,
        });

        Assert.Equal(1, await CountAsync(system, table, $"id = '{id}'"));
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("billing_audit_logs")]
    public async Task Tenant_CannotDisownItsOwnRow(string table)
    {
        var (id, owner) = (Guid.NewGuid(), Guid.NewGuid());
        await RowFactory.InsertAsync(_admin, table, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = owner,
        });
        await using var tenant = await ConnectAsync(_database.App, "tenant", owner);
        await using var command = tenant.CreateCommand();
        command.CommandText = $"UPDATE [{table}] SET tenant_id = NULL WHERE id = '{id}'";

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(33504, error.Number);
    }

    private sealed class PlainEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
        public byte[] EncryptBytes(string plainText) => [];
        public string DecryptBytes(byte[] cipherBytes) => "";
    }

    private sealed class RecordingHandler(Action? onHandle) : IOutboxMessageHandler
    {
        public string Type => "rls-test";

        public Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            onHandle?.Invoke();
            return Task.CompletedTask;
        }
    }

    private OutboxProcessor Processor(Action? onHandle = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(sp =>
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(_database.App, sql => sql.EnableRetryOnFailure(3))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>())
                .Options;
            var clock = new SystemDateTimeProvider();
            return new ApplicationDbContext(
                options,
                new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
                new SoftDeleteInterceptor(clock),
                new DomainEventDispatchInterceptor(new NoOpPublisher()),
                sp.GetRequiredService<ITenantContext>());
        });
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddSingleton<IEncryptionService, PlainEncryption>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<OutboxDispatchContext>();
        services.AddSingleton<IOutboxMessageHandler>(new RecordingHandler(onHandle));

        return new OutboxProcessor(
            services.BuildServiceProvider(),
            new ConfigurationBuilder().Build(),
            NullLogger<OutboxProcessor>.Instance);
    }

    private async Task<Guid> SeedTenantAsync()
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "tenants", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["status"] = "Active",
        });
        return id;
    }

    private async Task<Guid> SeedMessageAsync(Guid? tenant, int order, int attempts = 0)
    {
        var id = Guid.NewGuid();
        var due = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(order);
        await RowFactory.InsertAsync(_admin, "outbox_messages", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "rls-test",
            ["encrypted_payload"] = "{}",
            ["tenant_id"] = tenant,
            ["status"] = attempts == 0 ? "pending" : "processing",
            ["attempt_count"] = attempts,
            ["next_attempt_at"] = due,
            ["locked_until"] = attempts == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(-1),
            ["created_at"] = due,
        });
        return id;
    }

    private async Task<string> StatusAsync(Guid message)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = $"SELECT status FROM outbox_messages WHERE id = '{message}'";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Processor_FinishesABatchMixingTenantsAndUnownedMessages()
    {
        var (a, b) = (await SeedTenantAsync(), await SeedTenantAsync());
        var forA = await SeedMessageAsync(a, 1);
        var unowned = await SeedMessageAsync(null, 2);
        var forB = await SeedMessageAsync(b, 3);
        var unownedAfterB = await SeedMessageAsync(null, 4);

        await Processor().ProcessBatchAsync(default);

        Assert.Equal(
            ["published", "published", "published", "published"],
            [await StatusAsync(forA), await StatusAsync(unowned), await StatusAsync(forB), await StatusAsync(unownedAfterB)]);
    }

    [Fact]
    public async Task Processor_ParksAnotherTenantsMessageAfterSwitching()
    {
        var (a, b) = (await SeedTenantAsync(), await SeedTenantAsync());
        await SeedMessageAsync(a, 1);
        var exhausted = await SeedMessageAsync(b, 2, attempts: OutboxRetryPolicy.MaxAttempts);

        await Processor().ProcessBatchAsync(default);

        Assert.Equal("failed", await StatusAsync(exhausted));
    }

    [Fact]
    public async Task Processor_ReleasesAnotherTenantsMessagesOnShutdown()
    {
        var (a, b) = (await SeedTenantAsync(), await SeedTenantAsync());
        var forA = await SeedMessageAsync(a, 1);
        var forB = await SeedMessageAsync(b, 2);
        using var stopping = new CancellationTokenSource();

        // Stops after the first message, while the connection is in tenant A's context.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Processor(stopping.Cancel).ProcessBatchAsync(stopping.Token));

        Assert.Equal("published", await StatusAsync(forA));
        Assert.Equal("pending", await StatusAsync(forB));
    }
}
