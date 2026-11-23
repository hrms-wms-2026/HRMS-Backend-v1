using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Xunit;
using Xunit.Abstractions;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// An empty database booted in Production must hold the same reference data PostgreSQL did
/// (docs/reports/database/2026-11-07-postgres-reference-data.json).
/// </summary>
// Sets process-wide environment variables (Program.cs reads them before ConfigureWebHost runs),
// so it shares the WebApplicationFactory collection with every other test that does; its own
// SqlServerFixture comes as a class fixture.
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ReferenceDataParityTests(SqlServerFixture fixture, ITestOutputHelper output)
    : IClassFixture<SqlServerFixture>
{
    private static readonly string[] Settings =
    [
        "ASPNETCORE_ENVIRONMENT", "ConnectionStrings__DefaultConnection", "ConnectionStrings__MigrationConnection",
        "Encryption__MasterKey", "Jwt__Secret", "Jwt__TenantIssuer", "Jwt__TenantAudience",
        "TrayApp__Jwt__SigningKeys__0__Kid", "TrayApp__Jwt__SigningKeys__0__PrivateKeyPem", "TrayApp__Jwt__ActiveKid",
        "DevAdmin__Email", "DevAdmin__Password", "PlatformBootstrap__SuperAdminEmail",
        "PlatformBootstrap__SuperAdminFullName", "Tenancy__RootDomain", "AwsRekognition__Region",
        "AwsRekognition__LivenessRoleArn", "Urls__AppBaseUrl", "Urls__AdminConsoleBaseUrl", "Email__FromAddress",
        "AllowedOrigins__0", "AllowedOrigins__1", "BackgroundJobs__Enabled",
    ];

    // Generated on each run, or set from this test's configuration.
    private static bool IsGenerated(string column)
        => column is "id" or "created_by_id" or "updated_by_id" or "email" or "full_name"
            || column.EndsWith("_at", StringComparison.Ordinal)
            || column.EndsWith("_id", StringComparison.Ordinal);

    [Fact]
    public async Task ProductionBoot_MatchesPostgresReferenceData()
    {
        var database = await fixture.CreateDatabaseAsync();
        var previous = Settings.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Apply(database);
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
            using var client = factory.CreateClient();
            var health = await client.GetAsync("/health");
            Assert.True(health.IsSuccessStatusCode, $"Health endpoint returned {health.StatusCode}");

            await WaitForRowCountsToSettleAsync(database);
            var differences = await CompareAsync(database);
            foreach (var difference in differences)
            {
                output.WriteLine(difference);
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static void Apply(TestDatabase database)
    {
        var values = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__DefaultConnection"] = database.App,
            ["ConnectionStrings__MigrationConnection"] = database.Migrator,
            ["Encryption__MasterKey"] = "reference-parity-test-master-key-32-chars!!",
            ["Jwt__Secret"] = "reference-parity-test-jwt-secret-32-chars!!",
            ["Jwt__TenantIssuer"] = "crownus-api",
            ["Jwt__TenantAudience"] = "crownus-api",
            // Production needs a real ES256 key (SEC-15). Generated per run, with newlines escaped
            // the way an environment variable carries them.
            ["TrayApp__Jwt__SigningKeys__0__Kid"] = "reference-parity",
            ["TrayApp__Jwt__SigningKeys__0__PrivateKeyPem"] = NewEscapedP256Pem(),
            ["TrayApp__Jwt__ActiveKid"] = "reference-parity",
            ["DevAdmin__Email"] = "test_admin@onevo.dev",
            ["DevAdmin__Password"] = "test_password_123",
            ["PlatformBootstrap__SuperAdminEmail"] = "test_admin@onevo.dev",
            ["PlatformBootstrap__SuperAdminFullName"] = "Parity Test Admin",
            // Production refuses local hosts and empty URLs (DeploymentConfigurationValidator). The
            // host is never contacted: /health answers on any host.
            ["Tenancy__RootDomain"] = "crownus-parity-test.com",
            ["Urls__AppBaseUrl"] = "https://crownus-parity-test.com",
            ["Urls__AdminConsoleBaseUrl"] = "https://admin.crownus-parity-test.com",
            ["Email__FromAddress"] = "no-reply@crownus-parity-test.com",
            // A developer's .env may hold local http origins at these indexes; process values win over it.
            ["AllowedOrigins__0"] = "https://tools.crownus-parity-test.com",
            ["AllowedOrigins__1"] = "https://tools2.crownus-parity-test.com",
            // Compare what the boot seeds, not what recurring jobs write while it runs (job_leases rows, task 13).
            ["BackgroundJobs__Enabled"] = "false",
            ["AwsRekognition__Region"] = "us-east-1",
            ["AwsRekognition__LivenessRoleArn"] = "arn:aws:iam::000000000000:role/test",
        };
        foreach (var (key, value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string NewEscapedP256Pem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem().Replace("\r\n", "\n").Replace("\n", "\\n");
    }

    private static async Task<SqlConnection> OpenAdminAsync(TestDatabase database)
    {
        var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin");
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<List<string>> ListTablesAsync(SqlConnection connection)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.tables WHERE name <> '__EFMigrationsHistory' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<long> TotalRowsAsync(SqlConnection connection)
    {
        long total = 0;
        foreach (var table in await ListTablesAsync(connection))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT_BIG(*) FROM [{table}]";
            total += (long)(await command.ExecuteScalarAsync())!;
        }

        return total;
    }

    private static async Task WaitForRowCountsToSettleAsync(TestDatabase database)
    {
        long previous = -1;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await using var connection = await OpenAdminAsync(database);
            var total = await TotalRowsAsync(connection);
            if (total == previous && total > 0)
            {
                return;
            }

            previous = total;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new TimeoutException("Row counts did not settle after the Production boot.");
    }

    private static async Task<List<string>> CompareAsync(TestDatabase database)
    {
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            FindRepoRoot(), "docs", "reports", "database", "2026-11-07-postgres-reference-data.json")))!.AsObject();
        var differences = new List<string>();
        await using var connection = await OpenAdminAsync(database);
        var postBaselineTemplateIds = await ReadPostBaselineTemplateIdsAsync(connection);
        foreach (var table in await ListTablesAsync(connection))
        {
            var actual = (await ReadRowsAsync(connection, table))
                .Where(row => !IsAddedAfterBaseline(table, row, postBaselineTemplateIds))
                .ToList();
            var wanted = expected[table]?["rows"]?.AsArray().Select(row => row!.AsObject()).ToList() ?? [];
            if (actual.Count != wanted.Count)
            {
                differences.Add($"{table}: {actual.Count} rows, expected {wanted.Count}");
                continue;
            }

            var columns = wanted.FirstOrDefault()?.Select(pair => pair.Key).Where(c => !IsGenerated(c)).ToList() ?? [];
            var left = wanted.Select(row => Canonical(row, columns)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var right = actual.Select(row => Canonical(row, columns)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var mismatch = left.Zip(right).FirstOrDefault(pair => pair.First != pair.Second);
            if (mismatch != default)
            {
                differences.Add($"{table}: row differs{Environment.NewLine}  postgres:  {Cut(mismatch.First)}" +
                    $"{Environment.NewLine}  sqlserver: {Cut(mismatch.Second)}");
            }
        }

        return differences;
    }

    // Reference rows seeded for features built after the SQL Server baseline. They are not on PostgreSQL by
    // design. Add a permission code here in the same change that seeds it.
    private static readonly string[] PermissionCodesAddedAfterBaseline =
    [
        "support:manage", // 20261112072239_AddPlatformSupport
    ];

    // Notification templates seeded at startup for features built after the baseline. Their first published
    // version rows are excluded through template_id, which the versions table carries instead of the code.
    private static readonly string[] NotificationTemplateCodesAddedAfterBaseline =
    [
        "support_ticket_replied", // 20261112072239_AddPlatformSupport
        "support_ticket_status_changed", // 20261112072239_AddPlatformSupport
    ];

    private static bool IsAddedAfterBaseline(string table, JsonObject row, IReadOnlySet<string> postBaselineTemplateIds) =>
        table switch
        {
            "permissions" => PermissionCodesAddedAfterBaseline.Contains((string?)row["code"]),
            "module_permission_ownership" => PermissionCodesAddedAfterBaseline.Contains((string?)row["permission_code"]),
            "notification_templates" => NotificationTemplateCodesAddedAfterBaseline.Contains((string?)row["code"]),
            "notification_template_versions" => postBaselineTemplateIds.Contains(row["template_id"]?.ToString() ?? string.Empty),
            _ => false,
        };

    private static async Task<IReadOnlySet<string>> ReadPostBaselineTemplateIdsAsync(SqlConnection connection)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await ReadRowsAsync(connection, "notification_templates"))
        {
            if (NotificationTemplateCodesAddedAfterBaseline.Contains((string?)row["code"]))
            {
                ids.Add(row["id"]?.ToString() ?? string.Empty);
            }
        }

        return ids;
    }

    private static string Cut(string value) => value.Length > 600 ? value[..600] : value;

    private static async Task<List<JsonObject>> ReadRowsAsync(SqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT (SELECT * FROM [{table}] FOR JSON PATH, INCLUDE_NULL_VALUES)";
        command.CommandTimeout = 120;
        var json = await command.ExecuteScalarAsync() as string;
        return json is null ? [] : JsonNode.Parse(json)!.AsArray().Select(row => row!.AsObject()).ToList();
    }

    private static string Canonical(JsonObject row, IEnumerable<string> columns)
        => string.Join("|", columns.Select(column => $"{column}={Value(row[column])}"));

    // jsonb arrives as nested JSON from Postgres and as text from SQL Server; numbers may differ in scale.
    private static string Value(JsonNode? node)
    {
        if (node is null)
        {
            return "null";
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    return Sorted(JsonNode.Parse(text)!);
                }
                catch (JsonException)
                {
                    return text;
                }
            }

            return text;
        }

        if (node is JsonValue number && number.TryGetValue<decimal>(out var parsed))
        {
            return parsed.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);
        }

        return Sorted(node);
    }

    private static string Sorted(JsonNode node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}:{(p.Value is null ? "null" : Sorted(p.Value))}")) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(item => item is null ? "null" : Sorted(item))) + "]",
        _ => node.ToJsonString(),
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        // A git worktree has a .git file instead of a .git directory.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
