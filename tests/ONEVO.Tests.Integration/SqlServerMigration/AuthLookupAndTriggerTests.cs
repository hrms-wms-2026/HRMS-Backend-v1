using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

[Collection(SqlServerCollection.Name)]
public sealed class AuthLookupAndTriggerTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await using var cmd = TenantRlsInterceptor.SetTenantCommand(_admin, string.Empty, "admin");
        await cmd.ExecuteNonQueryAsync();
        await RowFactory.DisableConstraintsAsync(_admin);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedObjectiveAsync()
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "objectives", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = Tenant,
        });
        return id;
    }

    private async Task<Guid> SeedTaskAsync(Guid objective, Guid status, decimal hours, string kind = "task")
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "tasks", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = Tenant,
            ["objective_id"] = objective,
            ["status_id"] = status,
            ["estimated_hours"] = hours,
            ["progress_percent"] = 0,
            ["task_kind"] = kind,
            ["is_deleted"] = false,
            ["parent_task_id"] = null,
        });
        return id;
    }

    private async Task<Guid> SeedStatusAsync(bool complete)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "task_statuses", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tenant_id"] = Tenant,
            ["marks_task_complete"] = complete,
        });
        return id;
    }

    private Task<decimal> HoursAsync(Guid objective, string column)
        => ScalarAsync<decimal>($"SELECT {column} FROM wm_module_hour_summaries WHERE id = '{objective}'");

    [Fact]
    public async Task ObjectiveInsert_CreatesItsSummary()
    {
        var objective = await SeedObjectiveAsync();

        Assert.Equal(0, await HoursAsync(objective, "not_started_hours"));
    }

    [Fact]
    public async Task ModuleHourSummary_UpdatesOnTaskChange()
    {
        var objective = await SeedObjectiveAsync();
        var status = await SeedStatusAsync(complete: false);
        var task = await SeedTaskAsync(objective, status, 5);
        Assert.Equal(5, await HoursAsync(objective, "not_started_hours"));

        await ExecuteAsync($"UPDATE tasks SET progress_percent = 50 WHERE id = '{task}'");
        Assert.Equal(0, await HoursAsync(objective, "not_started_hours"));
        Assert.Equal(5, await HoursAsync(objective, "in_progress_hours"));

        await ExecuteAsync($"UPDATE tasks SET progress_percent = 100 WHERE id = '{task}'");
        Assert.Equal(5, await HoursAsync(objective, "completed_hours"));
    }

    [Fact]
    public async Task ModuleHourSummary_UpdatesBothObjectivesWhenATaskMoves()
    {
        var from = await SeedObjectiveAsync();
        var to = await SeedObjectiveAsync();
        var status = await SeedStatusAsync(complete: false);
        var task = await SeedTaskAsync(from, status, 3);

        await ExecuteAsync($"UPDATE tasks SET objective_id = '{to}' WHERE id = '{task}'");

        Assert.Equal(0, await HoursAsync(from, "not_started_hours"));
        Assert.Equal(3, await HoursAsync(to, "not_started_hours"));
    }

    [Fact]
    public async Task ModuleHourSummary_UpdatesOnTaskDelete()
    {
        var objective = await SeedObjectiveAsync();
        var status = await SeedStatusAsync(complete: false);
        var task = await SeedTaskAsync(objective, status, 2);

        await ExecuteAsync($"DELETE FROM tasks WHERE id = '{task}'");

        Assert.Equal(0, await HoursAsync(objective, "not_started_hours"));
    }

    [Fact]
    public async Task ModuleHourSummary_CountsMeetingHoursAsCompleted()
    {
        var objective = await SeedObjectiveAsync();
        var status = await SeedStatusAsync(complete: false);
        await SeedTaskAsync(objective, status, 4, kind: "meeting");

        Assert.Equal(4, await HoursAsync(objective, "completed_hours"));
        Assert.Equal(0, await ScalarAsync<int>(
            $"SELECT task_count FROM wm_module_hour_summaries WHERE id = '{objective}'"));
    }

    [Fact]
    public async Task ModuleHourSummary_UpdatesWhenStatusCompletionChanges()
    {
        var objective = await SeedObjectiveAsync();
        var status = await SeedStatusAsync(complete: false);
        await SeedTaskAsync(objective, status, 6);

        await ExecuteAsync($"UPDATE task_statuses SET marks_task_complete = 1 WHERE id = '{status}'");

        Assert.Equal(6, await HoursAsync(objective, "completed_hours"));
    }

    private async Task<(Guid Task, Guid Series)> SeedMeetingDetailsAsync()
    {
        var task = Guid.NewGuid();
        var series = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "meeting_details", new Dictionary<string, object?>
        {
            ["task_id"] = task,
            ["tenant_id"] = Tenant,
            ["series_id"] = series,
            ["original_start"] = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero),
        });
        return (task, series);
    }

    [Fact]
    public async Task MeetingDetailsKey_CannotChangeWithinASeries()
    {
        var (task, _) = await SeedMeetingDetailsAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            $"UPDATE meeting_details SET original_start = '2026-11-08T09:00:00+00:00' WHERE task_id = '{task}'"));

        Assert.Contains("trg_meeting_details_key_immutable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeetingDetailsTaskId_CannotChange()
    {
        var (task, _) = await SeedMeetingDetailsAsync();

        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            $"UPDATE meeting_details SET task_id = '{Guid.NewGuid()}' WHERE task_id = '{task}'"));
    }

    [Fact]
    public async Task MeetingDetailsKey_CanChangeWhenRemappedToAnotherSeries()
    {
        var (task, _) = await SeedMeetingDetailsAsync();

        await ExecuteAsync(
            $"UPDATE meeting_details SET series_id = '{Guid.NewGuid()}', original_start = '2026-11-08T09:00:00+00:00' " +
            $"WHERE task_id = '{task}'");
    }

    private async Task SeedLoginUserAsync(Guid tenant, string email, string status = "Active", bool active = true)
    {
        await RowFactory.InsertAsync(_admin, "tenants", new Dictionary<string, object?>
        {
            ["id"] = tenant,
            ["status"] = status,
        });
        await RowFactory.InsertAsync(_admin, "users", new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["tenant_id"] = tenant,
            ["email"] = email,
            ["is_active"] = active,
            ["is_deleted"] = false,
        });
    }

    [Fact]
    public async Task Lookup_ReturnsOneRowPerEligibleTenant_WithNoTenantContext()
    {
        const string email = "person@lookup.test";
        await SeedLoginUserAsync(Guid.NewGuid(), email);
        await SeedLoginUserAsync(Guid.NewGuid(), email, status: "Trial");
        await SeedLoginUserAsync(Guid.NewGuid(), email, status: "Suspended");
        await SeedLoginUserAsync(Guid.NewGuid(), email, active: false);

        await using var db = _database.CreateContext(_database.App);
        var rows = await new EfBaseLoginCandidateRepository(db).GetCandidatesAsync(email);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Lookup_NormalizesThroughTheComputedColumn()
    {
        await SeedLoginUserAsync(Guid.NewGuid(), "  Mixed.Case@Lookup.Test ");

        await using var db = _database.CreateContext(_database.App);
        var rows = await new EfBaseLoginCandidateRepository(db).GetCandidatesAsync("mixed.case@lookup.test");

        Assert.Single(rows);
    }

    [Fact]
    public async Task AppLogin_CannotReadUsers_WithoutTenantContext()
    {
        await SeedLoginUserAsync(Guid.NewGuid(), "hidden@lookup.test");
        await using var app = new SqlConnection(_database.App);
        await app.OpenAsync();
        await using var command = app.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users";

        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }
}
