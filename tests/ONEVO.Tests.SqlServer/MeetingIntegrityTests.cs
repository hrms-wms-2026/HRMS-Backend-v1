using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.SqlServer;

/// <summary>The rules PostgreSQL's deferred constraint triggers enforced, now checked just before commit.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class MeetingIntegrityTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private readonly Guid _objective = Guid.NewGuid();
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

    private ApplicationDbContext Db()
    {
        var context = new TenantContextAccessor();
        context.Resolve(new TenantRegistryEntry(_tenant, "t", TenantStatus.Active, null));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_database.App)
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

    private async Task<Guid> SeedTaskAsync(string kind)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "tasks", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = _tenant,
            ["project_id"] = _project,
            ["objective_id"] = _objective,
            ["task_kind"] = kind,
            ["is_deleted"] = false,
        });
        return id;
    }

    private async Task<(Guid Task, Guid Series)> SeedMeetingAsync()
    {
        var series = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "meeting_series", new Dictionary<string, object?>
        {
            ["id"] = series,
            ["tenant_id"] = _tenant,
            ["project_id"] = _project,
            ["objective_id"] = _objective,
            ["creation_path"] = "direct",
        });
        var task = await SeedTaskAsync("meeting");
        await RowFactory.InsertAsync(_admin, "meeting_details", new Dictionary<string, object?>
        {
            ["task_id"] = task,
            ["tenant_id"] = _tenant,
            ["series_id"] = series,
            ["original_start"] = DateTimeOffset.UtcNow,
            ["start_at"] = DateTimeOffset.UtcNow,
            ["end_at"] = DateTimeOffset.UtcNow.AddHours(1),
        });
        return (task, series);
    }

    private async Task<string> KindAsync(Guid task)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = $"SELECT task_kind FROM tasks WHERE id = '{task}'";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task MeetingTaskWithoutDetails_IsRefusedAtCommit()
    {
        var task = await SeedTaskAsync("task");
        await using var db = Db();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).TaskKind = "meeting";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("trg_meeting_task_has_details", error.Message, StringComparison.Ordinal);
        Assert.Equal("task", await KindAsync(task));
    }

    [Fact]
    public async Task TaskLeavingMeetingKindWithDetails_IsRefusedAtCommit()
    {
        var (task, _) = await SeedMeetingAsync();
        await using var db = Db();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).TaskKind = "task";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("trg_meeting_task_has_details", error.Message, StringComparison.Ordinal);
        Assert.Equal("meeting", await KindAsync(task));
    }

    [Fact]
    public async Task RemovingDetailsOfAMeetingTask_IsRefusedAtCommit()
    {
        var (task, _) = await SeedMeetingAsync();
        await using var db = Db();
        db.Remove(await db.Set<MeetingDetails>().SingleAsync(d => d.TaskId == task));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MovingAMeetingTaskAwayFromItsSeries_IsRefusedAtCommit()
    {
        var (task, _) = await SeedMeetingAsync();
        await using var db = Db();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).ObjectiveId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("trg_meeting_placement_matches_series", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovingASeriesAwayFromItsTasks_IsRefusedAtCommit()
    {
        var (_, series) = await SeedMeetingAsync();
        await using var db = Db();
        (await db.Set<MeetingSeries>().SingleAsync(s => s.Id == series)).ObjectiveId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MovingSeriesAndTaskTogetherInOneTransaction_Commits()
    {
        var (task, series) = await SeedMeetingAsync();
        var objective = Guid.NewGuid();
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();

        (await db.Set<MeetingSeries>().SingleAsync(s => s.Id == series)).ObjectiveId = objective;
        await db.SaveChangesAsync();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).ObjectiveId = objective;
        await db.SaveChangesAsync();

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task RuleBrokenInsideAnExplicitTransaction_IsRefusedAtCommitNotBefore()
    {
        var (task, _) = await SeedMeetingAsync();
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).ObjectiveId = Guid.NewGuid();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
    }

    [Fact]
    public async Task OrdinaryTaskEdit_RunsNoMeetingChecks()
    {
        var task = await SeedTaskAsync("task");
        await using var db = Db();
        (await db.Set<WorkTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == task)).Title = "Renamed";

        await db.SaveChangesAsync();

        Assert.Empty(db.PendingMeetingTaskIds);
    }
}
