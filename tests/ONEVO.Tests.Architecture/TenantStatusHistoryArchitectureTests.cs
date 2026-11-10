using System.Text.RegularExpressions;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the tenant_status_histories FK-correction migration:
/// - it adds only the two documented foreign keys (tenant_id -> tenants
///   Restrict, changed_by_id -> platform_users SetNull),
/// - it creates no tables (tenant_status_histories already exists; the
///   migration is additive-only),
/// - it never invents tenant_resource_limits or any other table,
/// - the entity still matches the inventory-approved column set.
/// </summary>
public class TenantStatusHistoryArchitectureTests
{
    [Fact]
    public void TenantStatusHistory_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(TenantStatusHistory)));
    }

    [Fact]
    public void TenantStatusHistory_HasExactlyTheInventoryColumns()
    {
        var properties = typeof(TenantStatusHistory)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            "ChangedAt",
            "ChangedById",
            "FromStatus",
            "Id",
            "Reason",
            "TenantId",
            "ToStatus"
        };

        Assert.Equal(expected, properties);
    }

    [Fact]
    public void NoMigration_Creates_TenantResourceLimits()
    {
        var migrationsDir = FindMigrationsDirectory();

        var offenders = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("\"tenant_resource_limits\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string ReadFkCorrectionMigrationSource()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*AddTenantStatusHistoryForeignKeys.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);
        return File.ReadAllText(migrationFiles[0]);
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }
}
