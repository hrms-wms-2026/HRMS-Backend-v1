using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Planner;

/// <summary>Day planner (2026-10-26 design, Part B): planned_task_blocks is tenant-isolated under forced RLS.</summary>
public sealed class PlannerSchemaIntegrationTests
{
    private static async Task<Guid> AddBlockAsync(MyTeamDb h)
    {
        await using var db = h.NewContext();
        var at = h.AddPlacement(db);
        await db.SaveChangesAsync();
        var task = h.AddTask(db, at.ProjectId, at.ObjectiveId, at.StatusId, progress: 0, due: null, assigneeEmployeeId: at.OrganizerEmployeeId);
        var start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var block = new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = at.OrganizerEmployeeId, TaskId = task.Id,
            StartAt = start, EndAt = start.AddHours(1), CreatedById = at.OrganizerEmployeeId,
        };
        db.PlannedTaskBlocks.Add(block);
        await db.SaveChangesAsync();
        return block.Id;
    }

    [Fact]
    public async Task A_tenant_sees_only_its_blocks()
    {
        var a = await MyTeamDb.CreateAsync();
        var b = await a.AddTenantAsync();
        var mine = await AddBlockAsync(a);
        await AddBlockAsync(b);

        await using var connection = TestDb.CreateConnection(a.ConnectionString);
        await connection.OpenAsync();
        await using (var role = new SqlCommand("EXECUTE AS USER = 'onevo_app'", connection))
            await role.ExecuteNonQueryAsync();
        await using (var mode = new SqlCommand(
            $"EXEC sp_set_session_context N'app.tenant_context_mode', N'tenant'; EXEC sp_set_session_context N'app.current_tenant_id', N'{a.TenantId}'",
            connection))
            await mode.ExecuteNonQueryAsync();

        var visible = new List<Guid>();
        await using (var select = new SqlCommand("SELECT id FROM planned_task_blocks", connection))
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                visible.Add(reader.GetGuid(0));

        Assert.Equal(new[] { mine }, visible);
    }
}
