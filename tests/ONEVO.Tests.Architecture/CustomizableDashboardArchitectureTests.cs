using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.Dashboard;
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

    private static readonly string[] RoleTemplateTables =
        ["dashboard_templates", "dashboard_template_widgets", "dashboard_template_widget_layouts"];

    [Fact]
    public void Role_template_tables_have_tenant_isolation_rls()
    {
        var source = ReadMigrationSource("AddRoleDashboardTemplates");

        Assert.Contains("CREATE POLICY tenant_isolation ON {table}", source);
        Assert.Contains("ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", source);
        Assert.Contains("ALTER TABLE {table} FORCE ROW LEVEL SECURITY", source);

        var arrayMatch = Regex.Match(source, @"TenantTables\s*=\s*\[(?<body>[\s\S]*?)\];");
        Assert.True(arrayMatch.Success, "Migration must declare a TenantTables array.");

        var covered = Regex.Matches(arrayMatch.Groups["body"].Value, "\"(?<name>[a-z0-9_]+)\"")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var table in RoleTemplateTables)
            Assert.Contains(table, covered);
    }

    [Fact]
    public void No_role_template_constraints_exist()
    {
        var source = ReadMigrationSource("AddRoleDashboardTemplates");

        // CHECK ties the flag to a null role_id (spec 4.1/4.2).
        var check = Regex.Match(source, @"CheckConstraint\(\s*""(?<name>[a-z0-9_]+)""\s*,\s*""(?<sql>[^""]*)""");
        Assert.True(check.Success, "Migration must declare a CHECK constraint.");
        Assert.Contains(Regex.Matches(source, @"CheckConstraint\(\s*""[a-z0-9_]+""\s*,\s*""(?<sql>[^""]*)""")
            .Select(m => m.Groups["sql"].Value),
            sql => sql.Contains("is_no_role_template", StringComparison.Ordinal)
                && sql.Contains("role_id IS NULL", StringComparison.Ordinal));

        // A filtered unique index on the flag enforces exactly one No-role template per tenant per dashboard.
        var filters = Regex.Matches(source, @"unique:\s*true,\s*filter:\s*""(?<filter>[^""]*)""")
            .Select(m => m.Groups["filter"].Value)
            .ToList();
        Assert.Contains(filters, f => f.Contains("is_no_role_template", StringComparison.Ordinal));
    }

    [Fact]
    public void Dashboard_layout_controller_requires_tenant_policy()
    {
        var type = typeof(DashboardLayoutController);
        Assert.Equal("TenantPolicy", type.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Equal("api/v1/dashboards", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        Assert.Empty(type.GetMethods().Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null));
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
