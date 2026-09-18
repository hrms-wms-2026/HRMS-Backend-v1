using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using ONEVO.Infrastructure.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Migrations;

/// <summary>Executes the actual migration SQL against a legacy table under FORCE RLS.
/// TASK_STATUS_TEST_POSTGRES may point at a disposable local PostgreSQL server when Docker
/// is unavailable. All fixture objects are created in a transaction and rolled back.</summary>
public sealed class TaskStatusGroupsMigrationTests
{
    [Fact]
    public async Task Backfill_RepairsLiveScopesWithDeterministicDoneAndCollisionSafeActiveUnderRls()
    {
        PostgreSqlContainer? container = null;
        var connectionString = Environment.GetEnvironmentVariable("TASK_STATUS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var suffix = Guid.NewGuid().ToString("N");
            var schema = "status_fixture_" + suffix;
            var role = "status_migrator_" + suffix;
            async Task Execute(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await command.ExecuteNonQueryAsync();
            }

            await Execute($"""
                CREATE ROLE {role} NOLOGIN NOSUPERUSER NOBYPASSRLS;
                CREATE SCHEMA {schema} AUTHORIZATION {role};
                SET LOCAL ROLE {role};
                SET LOCAL search_path = {schema};
                CREATE TABLE task_statuses (
                    id uuid PRIMARY KEY, project_id uuid NOT NULL, objective_id uuid,
                    name varchar(100) NOT NULL, display_order integer NOT NULL,
                    requires_approval boolean NOT NULL DEFAULT false, approver_id uuid,
                    marks_task_complete boolean NOT NULL DEFAULT false, tenant_id uuid NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz,
                    created_by_id uuid NOT NULL, is_deleted boolean NOT NULL DEFAULT false,
                    deleted_at timestamptz, visibility varchar(20) NOT NULL DEFAULT 'public'
                );
                CREATE UNIQUE INDEX one_name_per_scope ON task_statuses (tenant_id, project_id, objective_id, name);
                ALTER TABLE task_statuses ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_statuses FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON task_statuses
                    USING (current_setting('app.tenant_context_mode', true) = 'admin')
                    WITH CHECK (current_setting('app.tenant_context_mode', true) = 'admin');
                SET LOCAL app.tenant_context_mode = 'admin';
                """);

            var tenant = Guid.NewGuid();
            var project = Guid.NewGuid();
            var creator = Guid.NewGuid();
            var otherTenant = Guid.NewGuid();
            var scopes = new List<Guid?> { null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            async Task<Guid> Add(Guid? objective, string name, int order, bool complete = false, bool deleted = false, Guid? ownerTenant = null)
            {
                var id = Guid.NewGuid();
                await using var command = new NpgsqlCommand("""
                    INSERT INTO task_statuses (id, tenant_id, project_id, objective_id, name, display_order,
                        marks_task_complete, is_deleted, created_by_id)
                    VALUES (@id, @tenant, @project, @objective, @name, @order, @complete, @deleted, @creator)
                    """, connection, transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("tenant", ownerTenant ?? tenant);
                command.Parameters.AddWithValue("project", project);
                command.Parameters.AddWithValue("objective", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)objective ?? DBNull.Value);
                command.Parameters.AddWithValue("name", name);
                command.Parameters.AddWithValue("order", order);
                command.Parameters.AddWithValue("complete", complete);
                command.Parameters.AddWithValue("deleted", deleted);
                command.Parameters.AddWithValue("creator", creator);
                await command.ExecuteNonQueryAsync();
                return id;
            }

            // Existing Done is not last; fallback must not create a second Done.
            var existingDone = await Add(null, "Done", 1, true);
            await Add(null, "To Do", 0);
            await Add(null, "Review", 5);
            // Two-row repair and a historical missing completion flag.
            await Add(scopes[1], "To Do", 0);
            var fallbackDone = await Add(scopes[1], "Finished", 1);
            // Duplicate complete flags and tied orders are resolved deterministically.
            var duplicateA = await Add(scopes[2], "Finished A", 2, true);
            var duplicateB = await Add(scopes[2], "Finished B", 2, true);
            await Add(scopes[2], "Todo", 0);
            // Deleted rows cannot satisfy Active or Done; names still occupy unique index.
            await Add(scopes[3], "In Progress", 0);
            await Add(scopes[3], "Done", 1, true);
            await Add(scopes[3], "In Progress (1)", 2, false, true);
            await Add(scopes[3], "Deleted done", 3, true, true);
            // Singleton and entirely deleted scopes.
            await Add(scopes[4], "Only", 0);
            await Add(scopes[5], "Deleted only", 0, true, true);
            // Same project/scope across tenants must not interfere.
            await Add(null, "Other tenant only", 0, ownerTenant: otherTenant);

            await Execute("SET LOCAL app.tenant_context_mode = 'tenant';");
            await using (var hidden = new NpgsqlCommand("SELECT count(*) FROM task_statuses", connection, transaction))
                Assert.Equal(0L, await hidden.ExecuteScalarAsync());

            var migration = new AddTaskStatusCategoryAndColor();
            // Use migration operations so the SQL under test is never duplicated in the fixture.
            foreach (var column in migration.UpOperations.OfType<AddColumnOperation>())
                await Execute($"ALTER TABLE task_statuses ADD COLUMN {column.Name} {column.ColumnType};");
            foreach (var sql in migration.UpOperations.OfType<SqlOperation>())
                await Execute(sql.Sql);
            foreach (var column in migration.UpOperations.OfType<AlterColumnOperation>())
                await Execute($"ALTER TABLE task_statuses ALTER COLUMN {column.Name} SET NOT NULL;");

            var rows = new List<(Guid Id, Guid Tenant, Guid? Objective, string Name, string Category, bool Complete, bool Deleted, string Color)>();
            await using (var query = new NpgsqlCommand("SELECT id, tenant_id, objective_id, name, category, marks_task_complete, is_deleted, color FROM task_statuses", connection, transaction))
            await using (var reader = await query.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    rows.Add((reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetString(7)));

            foreach (var scope in rows.Where(r => !r.Deleted).GroupBy(r => (r.Tenant, r.Objective)))
            {
                Assert.Single(scope, r => r.Category == "done");
                Assert.Contains(scope, r => r.Category == "active");
            }
            Assert.All(rows, r => Assert.Equal(r.Category == "done", r.Complete));
            Assert.All(rows, r => Assert.Matches("^#[0-9A-Fa-f]{6}$", r.Color));
            Assert.Equal("done", rows.Single(r => r.Id == existingDone).Category);
            Assert.Equal("done", rows.Single(r => r.Id == fallbackDone).Category);
            var expectedDuplicate = new[] { duplicateA, duplicateB }.OrderBy(id => id).First();
            Assert.Equal(expectedDuplicate, rows.Single(r => r.Objective == scopes[2] && r.Category == "done").Id);
            Assert.Contains(rows, r => r.Objective == scopes[3] && r.Name == "In Progress (2)" && r.Category == "active" && !r.Deleted);
            Assert.DoesNotContain(rows, r => r.Objective == scopes[5] && !r.Deleted);
            Assert.Equal(4, rows.Count(r => r.Name.StartsWith("In Progress") && r.Category == "active"));
            await transaction.RollbackAsync();
        }
        finally
        {
            if (container is not null)
                await container.DisposeAsync();
        }
    }
}

