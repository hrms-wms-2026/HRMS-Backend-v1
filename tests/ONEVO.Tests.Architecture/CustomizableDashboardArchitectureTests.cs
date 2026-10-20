using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class CustomizableDashboardArchitectureTests
{
    private static readonly string[] DashboardTables =
        ["user_dashboards", "dashboard_widgets", "dashboard_widget_layouts"];

    [Fact]
    public void Dashboard_tables_have_tenant_isolation_rls()
    {
        var source = ReadMigrationSource("AddCustomizableDashboards");

        Assert.Contains("CREATE POLICY tenant_isolation ON {table}", source);
        Assert.Contains("ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", source);
        Assert.Contains("ALTER TABLE {table} FORCE ROW LEVEL SECURITY", source);

        var arrayMatch = Regex.Match(source, @"TenantTables\s*=\s*\[(?<body>[\s\S]*?)\];");
        Assert.True(arrayMatch.Success, "Migration must declare a TenantTables array.");

        var covered = Regex.Matches(arrayMatch.Groups["body"].Value, "\"(?<name>[a-z0-9_]+)\"")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var table in DashboardTables)
            Assert.Contains(table, covered);
    }

    private static string ReadMigrationSource(string migrationName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var migrationsDir = Path.Combine(directory.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(migrationsDir))
            {
                var file = Directory.GetFiles(migrationsDir, $"*_{migrationName}.cs").SingleOrDefault();
                Assert.True(file is not null, $"Migration {migrationName} was not found.");
                return File.ReadAllText(file!);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/ONEVO.Infrastructure/Migrations.");
    }
}
