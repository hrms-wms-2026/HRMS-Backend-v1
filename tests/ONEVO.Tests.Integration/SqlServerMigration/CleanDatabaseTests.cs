using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

[Collection(SqlServerCollection.Name)]
public sealed class CleanDatabaseTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Migrate_OnEmptyDatabase_Succeeds()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateMigratorContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task HasPendingModelChanges_IsFalse()
    {
        var database = await fixture.CreateDatabaseAsync(migrate: false);
        await using var context = database.CreateMigratorContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task EveryRegistryTable_HasAnEnabledPolicy()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(pr.target_object_id)
            FROM sys.security_policies p
            JOIN sys.security_predicates pr ON pr.object_id = p.object_id
            WHERE p.is_enabled = 1 AND pr.predicate_type_desc = 'FILTER'
            """;
        var covered = new HashSet<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                covered.Add(reader.GetString(0));
            }
        }

        Assert.Empty(RlsTableRegistry.Tables.Keys.Except(covered));
        Assert.Empty(covered.Except(RlsTableRegistry.Tables.Keys));
    }
}
