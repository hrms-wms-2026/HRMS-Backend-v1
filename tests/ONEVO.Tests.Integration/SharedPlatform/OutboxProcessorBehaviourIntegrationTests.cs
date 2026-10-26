using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// The claim-based processor keeps the outbox's existing behaviour: retry with backoff and park
/// after MaxAttempts, persist what handlers stage on the shared DbContext (under the message's
/// tenant, through RLS, as onevo_app), and process rows written before the claim columns existed.
/// It adds a handler time budget inside the lease and hands claims back on shutdown.
/// Requires Docker.
/// </summary>
public sealed class OutboxProcessorBehaviourIntegrationTests
{
    private const string FailingType = "outbox_it_failing";
    private const string HangingType = "outbox_it_hanging";
    private const string NotifyingType = "outbox_it_notifying";
    private const string LegacyType = "outbox_it_legacy";

    private sealed class FailingHandler : IOutboxMessageHandler
    {
        public string Type => FailingType;
        public Task HandleAsync(string payloadJson, CancellationToken ct) => throw new InvalidOperationException("provider said no");
    }

    /// <summary>Never finishes on its own: only cancellation ends it.</summary>
    /// <summary>Runs until it is cancelled. <paramref name="ran"/> receives how long it ran - the handler's own time,
    /// without the claim and the completion write around it.</summary>
    private sealed class HangingHandler(TaskCompletionSource started, Action<TimeSpan>? ran = null) : IOutboxMessageHandler
    {
        public string Type => HangingType;

        public async Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                ran?.Invoke(watch.Elapsed);
            }
        }
    }

    /// <summary>Stages a tenant-scoped Notification (FORCE RLS) on the shared DbContext and leaves
    /// saving it to the processor, like WorkNotificationOutboxHandler / NotificationDispatcher.</summary>
    private sealed class NotifyingHandler(ApplicationDbContext db, bool thenThrow) : IOutboxMessageHandler
    {
        public string Type => NotifyingType;

        public Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            var payload = JsonSerializer.Deserialize<NotifyPayload>(payloadJson)!;
            db.Notifications.Add(new Notification
            {
                Id = payload.NotificationId, TenantId = payload.TenantId, RecipientUserId = Guid.NewGuid(),
                TemplateCode = "outbox_it", Title = "t", Body = "b",
            });
            return thenThrow ? throw new InvalidOperationException("second half failed") : Task.CompletedTask;
        }
    }

    private sealed record NotifyPayload(Guid TenantId, Guid NotificationId);

    [Fact]
    public async Task FailedAttempt_ReturnsTheMessageToPending_WithTheExistingBackoff_AndClearsTheClaim()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(FailingType, host.TenantId);
        var processor = host.CreateProcessor(s => s.AddScoped<IOutboxMessageHandler, FailingHandler>());

        var before = DateTimeOffset.UtcNow;
        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));

        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("provider said no", row.LastError);
        Assert.InRange(row.NextAttemptAt, before.AddSeconds(29), DateTimeOffset.UtcNow.AddSeconds(31));
        Assert.Null(row.LockedBy);
        Assert.Null(row.LockedUntil);
        Assert.Null(row.ProcessedAt);
        // Backing off: not due again yet.
        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EighthFailedAttempt_ParksTheMessageAsFailed()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(FailingType);
        await using (var db = host.NewAdminContext())
        {
            var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == id);
            message.AttemptCount = OutboxRetryPolicy.MaxAttempts - 1;
            await db.SaveChangesAsync();
        }
        var processor = host.CreateProcessor(s => s.AddScoped<IOutboxMessageHandler, FailingHandler>());

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Failed, row.Status);
        Assert.Equal(OutboxRetryPolicy.MaxAttempts, row.AttemptCount);
        Assert.NotNull(row.ProcessedAt);
        Assert.Null(row.LockedBy);
    }

    [Fact]
    public async Task HandlerExceedingItsTimeBudget_IsCancelledInsideTheLease_AndCountedAsAFailedAttempt()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(HangingType);
        var started = new TaskCompletionSource();
        TimeSpan? handlerRan = null;
        var processor = host.CreateProcessor(
            s => s.AddScoped<IOutboxMessageHandler>(_ => new HangingHandler(started, elapsed => handlerRan = elapsed)), leaseSeconds: 3);

        // Safety net so a missing budget fails this test instead of hanging the run: this token
        // stands for host shutdown, which would surface as OperationCanceledException.
        using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await processor.ProcessBatchAsync(safetyNet.Token);

        // The lease is renewed right before the handler starts, so the handler's own run is what must end inside it.
        // Timing the whole batch would also count the claim and the first queries' warm-up.
        Assert.NotNull(handlerRan);
        Assert.True(handlerRan < processor.LeaseSettings.Lease, $"handler ran {handlerRan}, lease is {processor.LeaseSettings.Lease}");
        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Contains("time budget", row.LastError);
        Assert.Null(row.LockedBy);
    }

    [Fact]
    public async Task ShutdownMidBatch_HandsEveryClaimedMessageBack_WithoutCountingAnAttempt()
    {
        var host = await OutboxTestHost.CreateAsync();
        var ids = new[]
        {
            await host.SeedPendingAsync(HangingType),
            await host.SeedPendingAsync(HangingType),
            await host.SeedPendingAsync(HangingType),
        };
        var started = new TaskCompletionSource();
        var processor = host.CreateProcessor(s => s.AddScoped<IOutboxMessageHandler>(_ => new HangingHandler(started)));
        using var stopping = new CancellationTokenSource();

        var run = processor.ProcessBatchAsync(stopping.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await stopping.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        foreach (var id in ids)
        {
            var row = await host.GetAsync(id);
            Assert.Equal(OutboxMessageStatus.Pending, row.Status);
            Assert.Equal(0, row.AttemptCount);
            Assert.Null(row.LockedBy);
            Assert.Null(row.LockedUntil);
        }
    }

    [Fact]
    public async Task StagedTenantRows_AreSavedWithThePublishedOutcome_UnderTheMessagesTenantThroughRls()
    {
        var host = await OutboxTestHost.CreateAsync();
        Assert.True(await IsForceRlsAsync(host, "notifications"));
        var notificationId = Guid.NewGuid();
        var id = await host.SeedPendingAsync(NotifyingType, host.TenantId, new NotifyPayload(host.TenantId, notificationId));
        var processor = host.CreateProcessor(s =>
            s.AddScoped<IOutboxMessageHandler>(sp => new NotifyingHandler(sp.GetRequiredService<ApplicationDbContext>(), thenThrow: false)));

        await processor.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(OutboxMessageStatus.Published, (await host.GetAsync(id)).Status);
        await using var db = host.NewAdminContext();
        Assert.True(await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Id == notificationId && n.TenantId == host.TenantId));
    }

    [Fact]
    public async Task StagedTenantRows_OfAFailedAttempt_AreStillSavedWithTheFailure_AsBeforeClaiming()
    {
        var host = await OutboxTestHost.CreateAsync();
        var notificationId = Guid.NewGuid();
        var id = await host.SeedPendingAsync(NotifyingType, host.TenantId, new NotifyPayload(host.TenantId, notificationId));
        var processor = host.CreateProcessor(s =>
            s.AddScoped<IOutboxMessageHandler>(sp => new NotifyingHandler(sp.GetRequiredService<ApplicationDbContext>(), thenThrow: true)));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        await using var db = host.NewAdminContext();
        Assert.True(await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Id == notificationId));
    }

    [Fact]
    public async Task StagedRowsTheTenantContextCannotSave_FailTheAttempt_InsteadOfStallingTheBatch()
    {
        var host = await OutboxTestHost.CreateAsync();
        // No tenant on the message -> system mode -> RLS rejects the staged Notification.
        var bad = await host.SeedPendingAsync(NotifyingType, tenantId: null, new NotifyPayload(host.TenantId, Guid.NewGuid()));
        var processor = host.CreateProcessor(s =>
            s.AddScoped<IOutboxMessageHandler>(sp => new NotifyingHandler(sp.GetRequiredService<ApplicationDbContext>(), thenThrow: false)));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var row = await host.GetAsync(bad);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Null(row.LockedBy);
        Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task PendingRowWrittenBeforeTheClaimColumnsExisted_IsClaimedAndPublishedAfterTheMigration()
    {
        var host = await OutboxTestHost.CreateAsync();
        var id = Guid.NewGuid();
        await RunClaimLeaseMigrationAsync(host, down: true);
        await using (var db = host.NewAdminContext())
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var payload = OutboxTestHost.Encryption.Encrypt(JsonSerializer.Serialize(new TestPayload(id)));
            // The pre-migration shape of the table: no status 'processing', no lock columns.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO outbox_messages (id, type, encrypted_payload, tenant_id, status, attempt_count, created_at, next_attempt_at)
                VALUES ({id}, {LegacyType}, {payload}, {(Guid?)null}, 'pending', 0, {now}, {now})
                """);
        }
        await RunClaimLeaseMigrationAsync(host, down: false);

        var processed = new List<string>();
        var processor = host.CreateProcessor(s => s.AddScoped<IOutboxMessageHandler>(_ => new RecordingHandler(LegacyType, processed)));
        await processor.ProcessBatchAsync(CancellationToken.None);

        var expected = JsonSerializer.Serialize(new TestPayload(id));
        Assert.Equal(expected, Assert.Single(processed));
        var row = await host.GetAsync(id);
        Assert.Equal(OutboxMessageStatus.Published, row.Status);
        Assert.Null(row.LockedBy);
    }

    [Fact]
    public async Task RollingBackTheClaimMigration_HandsProcessingMessagesBackAsPending()
    {
        // Code from before claiming only reads pending rows, so a row left processing by the rollback would never be
        // sent again.
        var host = await OutboxTestHost.CreateAsync();
        var id = await host.SeedPendingAsync(LegacyType);
        var services = host.BuildServices(_ => { });
        await using (var scope = services.CreateAsyncScope())
            await OutboxClaimStore.ClaimBatchAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), "owner-a",
                TimeSpan.FromMinutes(5), 20, CancellationToken.None);

        await RunClaimLeaseMigrationAsync(host, down: true);

        await using var connection = new NpgsqlConnection(host.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM outbox_messages WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(OutboxMessageStatus.Pending, (string)(await command.ExecuteScalarAsync())!);
    }

    private const string ClaimLease = "20261018085329_AddOutboxClaimLease";
    private const string MigrationBeforeClaimLease = "20261018033708_AddNotificationTemplateVersioningAndRouting";

    /// <summary>Runs AddOutboxClaimLease's own Down (or Up) script against the test's database. Migrating the database
    /// itself below the claim columns would also roll back every later migration, and some of those are one-way
    /// (e.g. LeavePolicyPerTypeVersionsAssignments), so the test exercises only this migration's SQL.</summary>
    private static async Task RunClaimLeaseMigrationAsync(OutboxTestHost host, bool down)
    {
        string script;
        await using (var db = host.NewAdminContext())
        {
            var migrator = db.GetService<IMigrator>();
            script = down ? migrator.GenerateScript(ClaimLease, MigrationBeforeClaimLease)
                : migrator.GenerateScript(MigrationBeforeClaimLease, ClaimLease);
        }
        await using var connection = new NpgsqlConnection(host.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingHandler(string type, List<string> payloads) : IOutboxMessageHandler
    {
        public string Type => type;

        public Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            payloads.Add(payloadJson);
            return Task.CompletedTask;
        }
    }

    private static async Task<bool> IsForceRlsAsync(OutboxTestHost host, string table)
    {
        await using var connection = new NpgsqlConnection(host.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = @t AND relkind = 'r'", connection);
        command.Parameters.AddWithValue("t", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
