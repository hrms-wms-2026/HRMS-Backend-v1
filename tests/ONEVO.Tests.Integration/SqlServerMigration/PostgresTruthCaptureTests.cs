using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// One-off capture of what PostgreSQL really holds after the full migration chain, written to
/// docs/reports/database for the SQL Server parity checks. Removed with the provider swap.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class PostgresTruthCaptureTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task CapturePolicies()
    {
        var connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT coalesce(json_agg(r ORDER BY r."table", r.policy), '[]'::json)::text FROM (
                SELECT c.relname AS "table",
                       p.policyname AS policy,
                       p.cmd,
                       p.qual AS "using",
                       p.with_check AS "withCheck",
                       c.relrowsecurity AS "rlsEnabled",
                       c.relforcerowsecurity AS "forceRls"
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_policies p ON p.schemaname = n.nspname AND p.tablename = c.relname
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                  AND (c.relrowsecurity OR p.policyname IS NOT NULL)
            ) r
            """;
        var json = (string)(await command.ExecuteScalarAsync())!;
        var rows = JsonNode.Parse(json)!.AsArray();

        Assert.NotEmpty(rows);
        await WriteReportAsync("2026-11-07-postgres-policy-inventory.json", rows);
    }

    [Fact]
    public async Task CaptureReferenceData()
    {
        var connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        await using var environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable(
            "TrayApp__Jwt__Secret", "postgres-truth-capture-tray-secret-at-least-32-bytes");

        // The restricted app login, as on a real server, so seeders run under RLS.
        await using var factory = new ApiBootTestFactory(environmentScope.DefaultConnectionString)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();
        var health = await client.GetAsync("/health");
        Assert.True(health.IsSuccessStatusCode, $"Health endpoint returned {health.StatusCode}");

        await WaitForRowCountsToSettleAsync(connectionString);

        var result = new JsonObject();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var table in await ListTablesAsync(connection))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT coalesce(json_agg(t ORDER BY t::text), '[]'::json)::text FROM \"{table}\" t";
            var rows = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsArray();
            if (rows.Count == 0)
            {
                continue;
            }

            result[table] = new JsonObject { ["rowCount"] = rows.Count, ["rows"] = rows };
        }

        Assert.True(result.ContainsKey("permissions"), "Production boot left permissions empty.");
        await WriteReportAsync("2026-11-07-postgres-reference-data.json", result);
    }

    private static async Task WaitForRowCountsToSettleAsync(string connectionString)
    {
        long previous = -1;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            long total = 0;
            foreach (var table in await ListTablesAsync(connection))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT count(*) FROM \"{table}\"";
                total += (long)(await command.ExecuteScalarAsync())!;
            }

            if (total == previous && total > 0)
            {
                return;
            }

            previous = total;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new TimeoutException("Row counts did not settle after the Production boot.");
    }

    private static async Task<List<string>> ListTablesAsync(NpgsqlConnection connection)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
            ORDER BY tablename
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task WriteReportAsync(string fileName, JsonNode content)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "docs", "reports", "database", fileName);
        await File.WriteAllTextAsync(path, content.ToJsonString(Indented));
    }
}
