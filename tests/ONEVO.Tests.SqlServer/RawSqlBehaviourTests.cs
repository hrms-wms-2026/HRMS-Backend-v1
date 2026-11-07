using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.AppCatalog;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Planner;
using ONEVO.Infrastructure.Persistence.SqlServer;
using ONEVO.Infrastructure.Services.SharedPlatform.Jobs;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using Xunit;

namespace ONEVO.Tests.SqlServer;

/// <summary>Every rewritten statement, run as onevo_app under RLS against real SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class RawSqlBehaviourTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await using var mode = TenantRlsInterceptor.SetTenantCommand(_admin, string.Empty, "admin");
        await mode.ExecuteNonQueryAsync();
        await RowFactory.DisableConstraintsAsync(_admin);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private ApplicationDbContext Db(Guid? tenant = null)
    {
        var context = new TenantContextAccessor();
        context.Resolve(new TenantRegistryEntry(tenant ?? _tenant, "t", TenantStatus.Active, null));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_database.App, sql => sql.EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(context))
            .Options;
        var clock = new SystemDateTimeProvider();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            context);
    }

    private static async Task InTransactionAsync(ApplicationDbContext db, Func<Task> work)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await work();
            await transaction.CommitAsync();
        });
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task SeedAsync(string table, params (string Column, object? Value)[] values)
        => RowFactory.InsertAsync(_admin, table, values.ToDictionary(v => v.Column, v => v.Value));

    // ---- Concurrency ----

    [Fact]
    public async Task OutboxClaim_TwoWorkers_NeverClaimSameRow()
    {
        for (var i = 0; i < 300; i++)
        {
            await SeedAsync("outbox_messages",
                ("id", Guid.NewGuid()),
                ("status", "pending"),
                ("next_attempt_at", DateTimeOffset.UtcNow.AddMinutes(-1)),
                ("created_at", DateTimeOffset.UtcNow),
                ("deduplication_key", null));
        }

        var claimed = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            while (true)
            {
                await using var db = Db();
                var batch = await OutboxClaimStore.ClaimBatchAsync(db, $"w{worker}", TimeSpan.FromMinutes(5), 10, default);
                if (batch.Count == 0)
                {
                    return;
                }

                foreach (var message in batch)
                {
                    claimed.Add(message.Id);
                }
            }
        })));

        Assert.Equal(300, claimed.Count);
        Assert.Equal(300, claimed.Distinct().Count());
    }

    [Fact]
    public async Task OutboxClaim_ReclaimsAnExpiredLeaseAndCountsTheAttempt()
    {
        var id = Guid.NewGuid();
        await SeedAsync("outbox_messages",
            ("id", id),
            ("status", "processing"),
            ("locked_by", "gone"),
            ("locked_until", DateTimeOffset.UtcNow.AddMinutes(-1)),
            ("attempt_started_at", DateTimeOffset.UtcNow.AddMinutes(-2)),
            ("attempt_count", 1),
            ("next_attempt_at", DateTimeOffset.UtcNow.AddHours(-1)),
            ("created_at", DateTimeOffset.UtcNow));

        await using var db = Db();
        var batch = await OutboxClaimStore.ClaimBatchAsync(db, "new", TimeSpan.FromMinutes(5), 10, default);

        var message = Assert.Single(batch, m => m.Id == id);
        Assert.Equal(2, message.AttemptCount);
        Assert.Contains("gone", message.LastError, StringComparison.Ordinal);
    }

    private sealed class PlainEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
        public byte[] EncryptBytes(string plainText) => [];
        public string DecryptBytes(byte[] cipherBytes) => "";
    }

    [Fact]
    public async Task OutboxWrite_DuplicateKey_IsIgnored()
    {
        await using var db = Db();
        var writer = new OutboxWriter(db, new PlainEncryption(), new SystemDateTimeProvider());
        var key = Guid.NewGuid().ToString();
        bool first = false, second = true;

        await InTransactionAsync(db, async () =>
        {
            first = await writer.EnqueueKeyedAsync("type", new { a = 1 }, key);
            second = await writer.EnqueueKeyedAsync("type", new { a = 1 }, key);
        });

        Assert.True(first);
        Assert.False(second);
    }

    private JobLeaseService Leases(string instance)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Db());
        return new JobLeaseService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), instance);
    }

    [Fact]
    public async Task JobLease_OnlyOneHolder()
    {
        var job = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Leases($"i{i}").TryAcquireAsync(job, TimeSpan.FromMinutes(5), default)));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task JobLease_RenewAndReleaseLetsAnotherInstanceTakeIt()
    {
        var job = Guid.NewGuid().ToString("N");
        var first = Leases("first");
        var second = Leases("second");

        Assert.True(await first.TryAcquireAsync(job, TimeSpan.FromMinutes(5), default));
        Assert.True(await first.RenewAsync(job, TimeSpan.FromMinutes(5), default));
        Assert.False(await second.TryAcquireAsync(job, TimeSpan.FromMinutes(5), default));
        await first.ReleaseAsync(job, default);
        Assert.True(await second.TryAcquireAsync(job, TimeSpan.FromMinutes(5), default));
    }

    [Fact]
    public async Task ProjectCounter_ConcurrentIncrements_AreUnique()
    {
        var project = Guid.NewGuid();
        await SeedAsync("projects", ("id", project), ("tenant_id", _tenant), ("next_task_number", 1L));

        var numbers = await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
        {
            await using var db = Db();
            return await new EfProjectRepository(db).IncrementAndGetNextTaskNumberAsync(_tenant, project);
        }));

        Assert.Equal(30, numbers.Distinct().Count());
        Assert.Equal(1, numbers.Min());
    }

    [Fact]
    public async Task ProjectCounter_OfAnotherTenant_IsNotFound()
    {
        var project = Guid.NewGuid();
        await SeedAsync("projects", ("id", project), ("tenant_id", Guid.NewGuid()), ("next_task_number", 1L));

        await using var db = Db();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EfProjectRepository(db).IncrementAndGetNextTaskNumberAsync(_tenant, project));
    }

    [Fact]
    public async Task PlannerLock_SerialisesSameKey()
    {
        var employee = Guid.NewGuid();
        await using var holder = Db();
        await using var other = Db();
        await holder.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await holder.Database.BeginTransactionAsync();
            await new EfPlannerRepository(holder).LockPlanAsync(_tenant, employee);

            await other.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var otherTransaction = await other.Database.BeginTransactionAsync();
                Assert.False(await SqlAppLock.TryAcquireAsync(
                    other, EfPlannerRepository.PlanLockKey(_tenant, employee), SqlAppLock.NoWait, default));
            });

            await transaction.RollbackAsync();
        });
    }

    [Fact]
    public async Task MeetingLinkSyncLock_SecondCallerIsRefused()
    {
        var link = Guid.NewGuid();
        await using var holder = Db();
        await using var other = Db();
        await holder.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await holder.Database.BeginTransactionAsync();
            Assert.True(await new EfMeetingAttendanceRepository(holder).TryLockLinkSyncAsync(_tenant, link));

            await other.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var otherTransaction = await other.Database.BeginTransactionAsync();
                Assert.False(await new EfMeetingAttendanceRepository(other).TryLockLinkSyncAsync(_tenant, link));
            });

            await transaction.RollbackAsync();
        });
    }

    [Fact]
    public async Task ApprovalRow_LockIfPending_ReturnsTrueOnlyForPendingRows()
    {
        var pending = Guid.NewGuid();
        var approved = Guid.NewGuid();
        await SeedAsync("wm_approval_requests", ("id", pending), ("tenant_id", _tenant), ("status", "pending"));
        await SeedAsync("wm_approval_requests", ("id", approved), ("tenant_id", _tenant), ("status", "approved"));

        await using var db = Db();
        var repository = new EfWorkApprovalRequestRepository(db);
        await InTransactionAsync(db, async () =>
        {
            Assert.True(await repository.LockIfPendingAsync(_tenant, pending));
            Assert.False(await repository.LockIfPendingAsync(_tenant, approved));
        });
    }

    [Fact]
    public async Task StorageReservation_ConcurrentCallers_NeverExceedTheLimit()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = Db();
            return await new EfTenantStorageStatsRepository(db).TryReserveBytesAsync(_tenant, 100, 500);
        }));

        Assert.Equal(5, results.Count(won => won));
        Assert.Equal(500L, await ScalarAsync<long>(
            $"SELECT reserved_r2_bytes FROM tenant_storage_stats WHERE tenant_id = '{_tenant}'"));
    }

    // ---- Upserts ----

    [Fact]
    public async Task GlobalEmailUpsert_IsIdempotent()
    {
        await using var db = Db();
        var repository = new EfGlobalEmailDirectoryRepository(db);
        await repository.UpsertAsync("once@raw.test", _tenant);
        await repository.UpsertAsync("once@raw.test", _tenant);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM global_email_directory WHERE email = 'once@raw.test'"));
    }

    [Fact]
    public async Task ObservedApplication_KeepsTheLatestSeenTime()
    {
        var later = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var repository = new EfGlobalApplicationRepository(db);
        await repository.RecordObservedAsync(["raw-test.exe"], later, default);
        await repository.RecordObservedAsync(["raw-test.exe"], later.AddHours(-1), default);

        Assert.Equal(later, await ScalarAsync<DateTimeOffset>(
            "SELECT last_seen_at FROM global_app_catalog WHERE process_name = 'raw-test.exe'"));
    }

    // ---- Trees ----

    [Fact]
    public async Task DepartmentDescendants_WalkTheWholeTree()
    {
        var (root, child, grandchild, legalEntity) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var (id, parent) in new[] { (root, (Guid?)null), (child, root), (grandchild, child) })
        {
            await SeedAsync("departments",
                ("id", id), ("tenant_id", _tenant), ("legal_entity_id", legalEntity),
                ("parent_department_id", parent), ("is_active", true));
        }

        await using var db = Db();
        var repository = new EfDepartmentRepository(db);

        Assert.True(await repository.IsDescendantAsync(_tenant, legalEntity, root, grandchild));
        Assert.False(await repository.IsDescendantAsync(_tenant, legalEntity, grandchild, root));
        Assert.Equal(
            new[] { child, grandchild }.Order(),
            (await repository.GetDescendantDepartmentIdsAsync(_tenant, legalEntity, [root])).Order());
    }

    [Fact]
    public async Task PositionDescendants_WalkTheWholeTree()
    {
        var (root, child, legalEntity) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync("positions", ("id", root), ("tenant_id", _tenant), ("legal_entity_id", legalEntity));
        await SeedAsync("positions",
            ("id", child), ("tenant_id", _tenant), ("legal_entity_id", legalEntity), ("reports_to_position_id", root));

        await using var db = Db();

        Assert.True(await new EfPositionRepository(db).IsDescendantAsync(_tenant, legalEntity, root, child));
    }

    // ---- Meetings ----

    private MeetingOccurrenceDraft Draft(Guid series, DateTimeOffset start) => new(
        _tenant, series, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid().ToString("N")[..12], "Stand-up", null, Guid.NewGuid(),
        start, start, start.AddHours(1), false, null, false, null);

    [Fact]
    public async Task MeetingOccurrence_MaterializesOnceForASlot()
    {
        var series = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var writer = new EfMeetingOccurrenceWriter(db);
        Guid? first = null, second = Guid.NewGuid();

        await InTransactionAsync(db, async () =>
        {
            first = await writer.TryMaterializeAsync(Draft(series, start), default);
            second = await writer.TryMaterializeAsync(Draft(series, start), default);
        });

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM meeting_details WHERE series_id = '{series}'"));
    }

    [Fact]
    public async Task MeetingSeriesLock_IsHeldUntilTheTransactionEnds()
    {
        var series = Guid.NewGuid();
        await using var db = Db();
        var writer = new EfMeetingOccurrenceWriter(db);

        await InTransactionAsync(db, () => writer.LockSeriesAsync(_tenant, series, default));
        await InTransactionAsync(db, () => writer.LockSeriesAsync(_tenant, series, default));
    }

    [Fact]
    public async Task MeetingRemap_MovesOccurrencesIntoAnEmptySeries()
    {
        var (from, to) = (Guid.NewGuid(), Guid.NewGuid());
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        await using var db = Db();
        var writer = new EfMeetingOccurrenceWriter(db);
        Guid? task = null;
        await InTransactionAsync(db, async () => task = await writer.TryMaterializeAsync(Draft(from, start), default));

        await InTransactionAsync(db, () => writer.RemapIntoSeriesAsync(
            _tenant, to, [(task!.Value, start.AddDays(1), start.AddDays(1), start.AddDays(1).AddHours(1))], default));

        Assert.Equal(to, await ScalarAsync<Guid>($"SELECT series_id FROM meeting_details WHERE task_id = '{task}'"));
    }
}
