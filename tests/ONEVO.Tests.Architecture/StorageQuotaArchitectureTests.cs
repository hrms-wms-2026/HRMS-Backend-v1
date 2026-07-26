using System.Text.RegularExpressions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Storage.Quota.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Phase 1 storage quota rules:
/// - quota code lives in the documented feature folders,
/// - Application feature code never touches EF/DbContext (covered broadly by
///   LayerDependencyTests, re-asserted here for the quota namespace),
/// - the quota migration creates only the inventory-approved table,
/// - tenant_resource_limits (not in the Phase 1 inventory) is never invented,
/// - no Application code bypasses the quota service by using the stats
///   repository directly.
/// </summary>
public class StorageQuotaArchitectureTests
{
    [Fact]
    public void IStorageQuotaService_LivesInCommonServiceInterfaces()
    {
        Assert.Equal(
            "ONEVO.Application.Common.ServiceInterfaces",
            typeof(IStorageQuotaService).Namespace);
    }

    [Fact]
    public void StorageQuotaService_ImplementationLivesInInfrastructureServices()
    {
        var implementations = typeof(ONEVO.Infrastructure.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IStorageQuotaService).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, t =>
            Assert.StartsWith("ONEVO.Infrastructure.Services.Storage.Quota", t.Namespace ?? string.Empty));
    }

    [Fact]
    public void TenantStorageStats_IsTenantOwned_ForIsolationFilters()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(TenantStorageStats)));
    }

    [Fact]
    public void StorageQuotaApplicationTypes_DoNotReferenceDbContext()
    {
        var offenders = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features.Storage", StringComparison.Ordinal) == true)
            .SelectMany(GetDirectTypeReferences)
            .Where(reference =>
                reference.Contains("ApplicationDbContext") ||
                reference.Contains("DbSet") ||
                reference.Contains("Microsoft.EntityFrameworkCore"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoApplicationType_BypassesQuotaService_ByUsingStatsRepositoryDirectly()
    {
        // The stats repository is a read model for the quota service only.
        // Handlers must consume IStorageQuotaService, never the raw counters.
        var statsRepositoryName =
            "ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces.ITenantStorageStatsRepository";

        var offenders = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features.", StringComparison.Ordinal) == true)
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features.Storage.Quota", StringComparison.Ordinal) != true)
            .SelectMany(t => GetDirectTypeReferences(t)
                .Where(reference => reference.Contains(statsRepositoryName))
                .Select(reference => $"{t.FullName} -> {reference}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void StorageQuotaMigration_CreatesOnly_TenantStorageStats()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*AddTenantStorageStats.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);

        var source = File.ReadAllText(migrationFiles[0]);
        var createdTables = Regex.Matches(source, "CreateTable\\s*\\(\\s*name:\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.Equal(["tenant_storage_stats"], createdTables);
    }

    [Fact]
    public void StorageQuotaMigration_AddsForeignKey_ToTenants()
    {
        // Inventory: tenant_storage_stats.tenant_id = uuid, PK, FK -> tenants.
        var source = ReadStorageQuotaMigrationSource();

        var foreignKey = Regex.Match(
            source,
            "ForeignKey\\s*\\((?<body>[^;]*?)\\)",
            RegexOptions.Singleline);

        Assert.True(foreignKey.Success, "the migration must declare a foreign key");
        Assert.Contains("column: x => x.tenant_id", foreignKey.Groups["body"].Value);
        Assert.Contains("principalTable: \"tenants\"", foreignKey.Groups["body"].Value);
        Assert.Contains("principalColumn: \"id\"", foreignKey.Groups["body"].Value);

        Assert.Contains("PrimaryKey(\"pk_tenant_storage_stats\", x => x.tenant_id)", source);
    }

    [Fact]
    public void TenantStorageStats_HasExactlyTheInventoryColumns()
    {
        var source = ReadStorageQuotaMigrationSource();

        var columns = Regex.Matches(source, "(?<name>\\w+) = table\\.Column<")
            .Select(m => m.Groups["name"].Value)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            "last_calculated_at",
            "reserved_r2_bytes",
            "tenant_id",
            "updated_at",
            "used_db_bytes",
            "used_r2_bytes"
        };

        Assert.Equal(expected, columns);
    }

    private static string ReadStorageQuotaMigrationSource()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*AddTenantStorageStats.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(migrationFiles);
        return File.ReadAllText(migrationFiles[0]);
    }

    [Fact]
    public void EfTenantStorageStatsRepository_HasNoExpressionBodiedMembers()
    {
        // Scoped guard: this repository controls storage quota counters and
        // intentionally uses raw SQL for atomic reserve/release/commit
        // behavior, so its members must stay in readable block-bodied form.
        // This rule is scoped only to this repository, not the whole codebase.
        var source = ReadTenantStorageStatsRepositorySource();

        var expressionBodiedMembers = Regex.Matches(
            source,
            @"^\s*(public|private|protected|internal)[^\{;=]*\)\s*=>",
            RegexOptions.Multiline);

        Assert.True(
            expressionBodiedMembers.Count == 0,
            "EfTenantStorageStatsRepository must not contain expression-bodied members: " +
            string.Join(", ", expressionBodiedMembers.Select(m => m.Value.Trim())));
    }

    [Fact]
    public void EfTenantStorageStatsRepository_PreservesAtomicSqlBehavior()
    {
        // Guards that the intentional raw-SQL atomicity for quota
        // reserve/release/commit was not replaced by EF load-modify-save
        // logic during the readability cleanup.
        var source = ReadTenantStorageStatsRepositorySource();

        Assert.Contains("ExecuteSqlInterpolatedAsync", source);
        Assert.Contains("ON CONFLICT", source);
        Assert.Contains("GREATEST(0", source);
        Assert.Contains("tenant_storage_stats", source);
        Assert.Contains("reserved_r2_bytes", source);
        Assert.Contains("used_r2_bytes", source);
    }

    private static string ReadTenantStorageStatsRepositorySource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "Storage",
                "Quota",
                "EfTenantStorageStatsRepository.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfTenantStorageStatsRepository.cs above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void NoMigration_Creates_TenantResourceLimits()
    {
        // tenant_resource_limits is referenced by architecture docs but is NOT
        // in the Phase 1 table inventory; it must not be invented by a
        // migration until the inventory approves it.
        var migrationsDir = FindMigrationsDirectory();

        var offenders = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("\"tenant_resource_limits\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
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

    private static IEnumerable<string> GetDirectTypeReferences(Type type)
    {
        foreach (var field in type.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            yield return field.FieldType.FullName ?? field.FieldType.Name;
        }

        foreach (var constructor in type.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }

        foreach (var method in type.GetMethods(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType.FullName ?? method.ReturnType.Name;

            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }
    }
}
