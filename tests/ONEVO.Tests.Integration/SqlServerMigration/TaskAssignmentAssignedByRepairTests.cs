using FluentAssertions;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The hand-written data repair in migration AddCoreForeignKeys39b: old seeded task_assignments rows
/// hold the assigner's user id in assigned_by_id, which must become that user's employee id before
/// fk_task_assignments_employees_assigned_by_id can be created. The test database already has the key,
/// so each test switches it off (NOCHECK), writes the old data, runs the repair, and checks the key again.
/// Requires Docker.
/// </summary>
public sealed class TaskAssignmentAssignedByRepairTests
{
    private const string ForeignKey = "fk_task_assignments_employees_assigned_by_id";

    [Fact]
    public async Task Repair_replaces_a_user_id_with_that_users_employee_and_keeps_real_employee_ids()
    {
        var s = await SeedAsync();
        await ExecAsync(s.Db, $"""
            ALTER TABLE dbo.task_assignments NOCHECK CONSTRAINT {ForeignKey};
            UPDATE dbo.task_assignments SET assigned_by_id = '{s.AssignerUserId}' WHERE id = '{s.LegacyAssignmentId}';
            """);

        await ExecAsync(s.Db, AddCoreForeignKeys39b.RepairTaskAssignmentAssignedBySql);

        (await AssignedByAsync(s.Db, s.LegacyAssignmentId)).Should().Be(s.AssignerEmployeeId);
        (await AssignedByAsync(s.Db, s.CurrentAssignmentId)).Should().Be(s.AssignerEmployeeId,
            "a row that already holds an employee id is left alone");

        var recheck = () => ExecAsync(s.Db, $"ALTER TABLE dbo.task_assignments WITH CHECK CHECK CONSTRAINT {ForeignKey};");
        await recheck.Should().NotThrowAsync("every row now points at an employee");
    }

    [Fact]
    public async Task Repair_stops_with_a_count_when_a_value_is_neither_an_employee_nor_a_users_employee()
    {
        var s = await SeedAsync();
        var unknown = Guid.NewGuid();
        await ExecAsync(s.Db, $"""
            ALTER TABLE dbo.task_assignments NOCHECK CONSTRAINT {ForeignKey};
            UPDATE dbo.task_assignments SET assigned_by_id = '{unknown}' WHERE id = '{s.LegacyAssignmentId}';
            """);

        var repair = () => ExecAsync(s.Db, AddCoreForeignKeys39b.RepairTaskAssignmentAssignedBySql);

        (await repair.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(50039);
        (await AssignedByAsync(s.Db, s.LegacyAssignmentId)).Should().Be(unknown, "an unknown value is never guessed");
    }

    private sealed record Seed(MyTeamDb Db, Guid AssignerEmployeeId, Guid AssignerUserId, Guid LegacyAssignmentId, Guid CurrentAssignmentId);

    private static async Task<Seed> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var assigner = helper.AddEmployee(db);
        var assignee = helper.AddEmployee(db);
        var (projectId, rootId) = helper.AddProject(db, assigner.Id);
        var status = helper.AddStatus(db, projectId, marksComplete: false);
        helper.AddTask(db, projectId, rootId, status, 0, null, assigneeEmployeeId: assignee.Id);
        helper.AddTask(db, projectId, rootId, status, 0, null, assigneeEmployeeId: assignee.Id);
        var assignments = db.TaskAssignments.Local.ToList();
        foreach (var assignment in assignments)
        {
            assignment.AssignedById = assigner.Id;
        }

        await db.SaveChangesAsync();
        return new Seed(helper, assigner.Id, assigner.UserId, assignments[0].Id, assignments[1].Id);
    }

    private static async Task ExecAsync(MyTeamDb helper, string sql)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> AssignedByAsync(MyTeamDb helper, Guid assignmentId)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT assigned_by_id FROM dbo.task_assignments WHERE id = '{assignmentId}';";
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}
