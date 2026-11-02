using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using ONEVO.Infrastructure.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Migrations;

/// <summary>Executes the AddTaskCategoryKind backfill SQL against minimal tables under FORCE RLS.
/// TASK_STATUS_TEST_POSTGRES may point at a disposable local PostgreSQL server when Docker
/// is unavailable. All fixture objects are created in a transaction and rolled back.</summary>
public sealed class AddTaskCategoryKindMigrationTests
{
    [Fact]
    public async Task Backfill_GivesChecklistOnlyToTheOfficeProjectsOnboardingAndOffboarding()
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
            var schema = "category_kind_fixture_" + suffix;
            var role = "category_kind_migrator_" + suffix;
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
                CREATE TABLE projects (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, system_purpose varchar(20));
                CREATE TABLE task_categories (
                    id uuid PRIMARY KEY, tenant_id uuid NOT NULL, project_id uuid NOT NULL,
                    name varchar(100) NOT NULL, system_key varchar(20)
                );
                ALTER TABLE projects ENABLE ROW LEVEL SECURITY;
                ALTER TABLE projects FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON projects
                    USING (current_setting('app.tenant_context_mode', true) = 'admin')
                    WITH CHECK (current_setting('app.tenant_context_mode', true) = 'admin');
                ALTER TABLE task_categories ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_categories FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON task_categories
                    USING (current_setting('app.tenant_context_mode', true) = 'admin')
                    WITH CHECK (current_setting('app.tenant_context_mode', true) = 'admin');
                SET LOCAL app.tenant_context_mode = 'admin';
                """);

            var tenant = Guid.NewGuid();
            var office = Guid.NewGuid();
            var normal = Guid.NewGuid();
            async Task AddProject(Guid id, string? purpose)
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO projects (id, tenant_id, system_purpose) VALUES (@id, @tenant, @purpose)", connection, transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("tenant", tenant);
                command.Parameters.AddWithValue("purpose", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)purpose ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }
            async Task<Guid> AddCategory(Guid project, string name, string? systemKey = null)
            {
                var id = Guid.NewGuid();
                await using var command = new NpgsqlCommand("""
                    INSERT INTO task_categories (id, tenant_id, project_id, name, system_key)
                    VALUES (@id, @tenant, @project, @name, @systemKey)
                    """, connection, transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("tenant", tenant);
                command.Parameters.AddWithValue("project", project);
                command.Parameters.AddWithValue("name", name);
                command.Parameters.AddWithValue("systemKey", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)systemKey ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
                return id;
            }

            await AddProject(office, "office");
            await AddProject(normal, null);
            var expected = new Dictionary<Guid, string>
            {
                [await AddCategory(office, "Onboarding")] = "checklist",
                [await AddCategory(office, "Offboarding")] = "checklist",
                [await AddCategory(office, "Meeting", "meeting")] = "meeting",
                // A user's category in a normal project is never a system kind by its name.
                [await AddCategory(normal, "Onboarding")] = "task",
                [await AddCategory(normal, " offboarding ")] = "task",
                [await AddCategory(normal, "meeting", "meeting")] = "meeting",
                [await AddCategory(normal, "Bugs")] = "bug",
                [await AddCategory(normal, "User Story")] = "story",
                [await AddCategory(normal, "Enhancement")] = "feature",
                [await AddCategory(normal, "QA")] = "testing",
                [await AddCategory(normal, "Design")] = "task",
            };

            await Execute("SET LOCAL app.tenant_context_mode = 'tenant';");
            await using (var hidden = new NpgsqlCommand("SELECT count(*) FROM task_categories", connection, transaction))
                Assert.Equal(0L, await hidden.ExecuteScalarAsync());

            var migration = new AddTaskCategoryKind();
            // Use migration operations so the SQL under test is never duplicated in the fixture.
            foreach (var column in migration.UpOperations.OfType<AddColumnOperation>())
                await Execute($"ALTER TABLE task_categories ADD COLUMN {column.Name} {column.ColumnType} NOT NULL DEFAULT '{column.DefaultValue}';");
            foreach (var sql in migration.UpOperations.OfType<SqlOperation>())
                await Execute(sql.Sql);

            var actual = new Dictionary<Guid, string>();
            await using (var query = new NpgsqlCommand("SELECT id, kind FROM task_categories", connection, transaction))
            await using (var reader = await query.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    actual[reader.GetGuid(0)] = reader.GetString(1);

            Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
            await transaction.RollbackAsync();
        }
        finally
        {
            if (container is not null)
                await container.DisposeAsync();
        }
    }
}
