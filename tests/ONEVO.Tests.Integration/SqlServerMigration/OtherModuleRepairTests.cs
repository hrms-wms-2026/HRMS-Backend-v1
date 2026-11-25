using FluentAssertions;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The hand-written SQL of migrations AddOtherModuleForeignKeys39e (repair) and AddStatusCheckConstraints39f (read-only
/// check). The test database already has the keys and constraints, so each test switches one off (NOCHECK) and writes
/// the bad value in the 'admin' tenant context mode (RLS applies to every login on SQL Server). Requires Docker.
/// </summary>
public sealed class OtherModuleRepairTests
{
    private const string SuspendedByKey = "fk_users_users_suspended_by_id";
    private const string ScopeCheck = "ck_clock_in_policies_scope_type";
    private const string Admin = "EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';";
    private const string Reset = "EXEC sp_set_session_context N'app.tenant_context_mode', NULL;";

    [Fact]
    public async Task Repair_passes_on_clean_data()
    {
        var (db, _, _) = await SeedAsync();

        var repair = () => ExecAsync(db, AddOtherModuleForeignKeys39e.RepairOtherModuleReferencesSql);

        await repair.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Repair_sets_guid_empty_to_null()
    {
        var (db, userId, _) = await SeedAsync();
        await SetSuspendedByAsync(db, userId, "00000000-0000-0000-0000-000000000000");

        await ExecAsync(db, AddOtherModuleForeignKeys39e.RepairOtherModuleReferencesSql);

        (await SuspendedByAsync(db, userId)).Should().BeNull();
    }

    [Fact]
    public async Task Repair_sets_an_id_of_another_identity_table_to_null()
    {
        var (db, userId, employeeId) = await SeedAsync();
        // users.suspended_by_id points at users; an employees id is the wrong kind.
        await SetSuspendedByAsync(db, userId, employeeId.ToString());

        await ExecAsync(db, AddOtherModuleForeignKeys39e.RepairOtherModuleReferencesSql);

        (await SuspendedByAsync(db, userId)).Should().BeNull();
    }

    [Fact]
    public async Task Repair_stops_on_an_unknown_id_and_deletes_nothing()
    {
        var (db, userId, _) = await SeedAsync();
        var unknown = Guid.NewGuid();
        await SetSuspendedByAsync(db, userId, unknown.ToString());

        var repair = () => ExecAsync(db, AddOtherModuleForeignKeys39e.RepairOtherModuleReferencesSql);

        var thrown = (await repair.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50039);
        thrown.Message.Should().Contain("users.suspended_by_id=1");
        (await SuspendedByAsync(db, userId)).Should().Be(unknown, "the repair never removes a row or guesses a parent");
    }

    [Fact]
    public async Task Status_check_passes_on_clean_data()
    {
        var (db, _, _) = await SeedAsync();

        var check = () => ExecAsync(db, AddStatusCheckConstraints39f.CheckStatusValuesSql);

        await check.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Status_check_stops_and_names_the_column_and_changes_nothing()
    {
        var (db, _, _) = await SeedAsync();
        await ExecAsync(db, $"""
            {Admin}
            ALTER TABLE dbo.clock_in_policies NOCHECK CONSTRAINT {ScopeCheck};
            UPDATE dbo.clock_in_policies SET scope_type = 'not_a_scope';
            {Reset}
            """);

        var check = () => ExecAsync(db, AddStatusCheckConstraints39f.CheckStatusValuesSql);

        var thrown = (await check.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50039);
        thrown.Message.Should().Contain("clock_in_policies.scope_type=1");
        (await ScalarAsync(db, "SELECT TOP 1 scope_type FROM dbo.clock_in_policies")).Should().Be("not_a_scope");
    }

    private static async Task<(MyTeamDb Db, Guid UserId, Guid EmployeeId)> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var employee = helper.AddEmployee(db);
        helper.AddClockInPolicy(db);
        await db.SaveChangesAsync();
        return (helper, MyTeamDb.UserIdOf(db, employee.Id), employee.Id);
    }

    private static Task SetSuspendedByAsync(MyTeamDb db, Guid userId, string value) => ExecAsync(db, $"""
        {Admin}
        ALTER TABLE dbo.users NOCHECK CONSTRAINT {SuspendedByKey};
        UPDATE dbo.users SET suspended_by_id = '{value}' WHERE id = '{userId}';
        {Reset}
        """);

    private static async Task<Guid?> SuspendedByAsync(MyTeamDb db, Guid userId)
        => await ScalarAsync(db, $"SELECT suspended_by_id FROM dbo.users WHERE id = '{userId}'") as Guid?;

    private static async Task ExecAsync(MyTeamDb helper, string sql)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(MyTeamDb helper, string select)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"{Admin} {select};";
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }
}
