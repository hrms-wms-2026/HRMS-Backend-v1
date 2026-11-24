using FluentAssertions;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The hand-written check in migration AddTenantForeignKeys39d: it counts tenant_id values that are not a tenants row
/// and stops before any key is added. The test database already has the keys, so each test switches one off
/// (NOCHECK) on project_categories and writes the data in the 'admin' tenant context mode (RLS applies to every login
/// on SQL Server). Requires Docker.
/// </summary>
public sealed class TenantOrphanCheckTests
{
    private const string ForeignKey = "fk_project_categories_tenants_tenant_id";
    private const string Admin = "EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';";
    private const string Reset = "EXEC sp_set_session_context N'app.tenant_context_mode', NULL;";

    [Fact]
    public async Task Check_passes_when_every_tenant_id_is_a_tenant()
    {
        var (db, _) = await SeedAsync();

        var check = () => ExecAsync(db, AddTenantForeignKeys39d.CheckTenantOrphansSql);

        await check.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Check_stops_and_names_the_table_and_changes_nothing()
    {
        var (db, categoryId) = await SeedAsync();
        var unknown = Guid.NewGuid();
        await ExecAsync(db, $"""
            {Admin}
            ALTER TABLE dbo.project_categories NOCHECK CONSTRAINT {ForeignKey};
            UPDATE dbo.project_categories SET tenant_id = '{unknown}' WHERE id = '{categoryId}';
            {Reset}
            """);

        var check = () => ExecAsync(db, AddTenantForeignKeys39d.CheckTenantOrphansSql);

        var thrown = (await check.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50039);
        thrown.Message.Should().Contain("project_categories=1");
        (await TenantOfAsync(db, categoryId)).Should().Be(unknown, "the check never changes or removes a row");
    }

    private static async Task<(MyTeamDb Db, Guid CategoryId)> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var lead = helper.AddEmployee(db);
        var (projectId, _) = helper.AddProject(db, lead.Id);
        var categoryId = db.Projects.Local.Single(p => p.Id == projectId).CategoryId;
        await db.SaveChangesAsync();
        return (helper, categoryId);
    }

    private static async Task ExecAsync(MyTeamDb helper, string sql)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> TenantOfAsync(MyTeamDb helper, Guid categoryId)
    {
        await using var connection = TestDb.CreateConnection(helper.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"{Admin} SELECT tenant_id FROM dbo.project_categories WHERE id = '{categoryId}';";
        var value = await command.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }
}
