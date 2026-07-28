using System.Text.RegularExpressions;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class DevSmokeTestTenantSeederArchitectureTests
{
    [Fact]
    public void StartAsync_EntersAdminModeBeforeInvokingTheBootstrapRoutine()
    {
        var source = ReadSeederSource();
        var contextResolution = source.IndexOf(
            "GetRequiredService<IWritableTenantContext>()",
            StringComparison.Ordinal);
        var adminMode = source.IndexOf(
            "tenantContext.SetAdminMode();",
            contextResolution,
            StringComparison.Ordinal);
        var seedInvocation = source.IndexOf(
            "await SeedAsync(",
            contextResolution,
            StringComparison.Ordinal);

        Assert.True(contextResolution >= 0);
        Assert.True(adminMode > contextResolution);
        Assert.True(seedInvocation > adminMode);
    }

    [Fact]
    public void Seeder_EstablishesAdminAndTenantContextsBeforeCorrespondingWrites()
    {
        var source = ReadSeederSource();
        var adminMode = source.IndexOf("tenantContext.SetAdminMode();", StringComparison.Ordinal);
        var tenantSeed = source.IndexOf("SeedTenantAsync(db", StringComparison.Ordinal);
        var tenantMode = source.IndexOf("ResolveSmokeTenantContext(tenantContext, tenant);", StringComparison.Ordinal);
        var tenantUserSeed = source.IndexOf("SeedTenantUserAsync(db", StringComparison.Ordinal);

        Assert.True(adminMode >= 0);
        Assert.True(tenantSeed > adminMode);
        Assert.True(tenantMode > tenantSeed);
        Assert.True(tenantUserSeed > tenantMode);
        Assert.Contains("GetRequiredService<IWritableTenantContext>()", source, StringComparison.Ordinal);
        Assert.Contains("new TenantRegistryEntry(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Seeder_DoesNotWeakenRlsOrUsePrivilegedDatabaseConnections()
    {
        var source = ReadSeederSource();

        Assert.DoesNotContain("DISABLE ROW LEVEL SECURITY", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BYPASSRLS", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NpgsqlConnection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Username=postgres", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MigrateAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex("(CREATE|ALTER|DROP)\\s+TABLE", RegexOptions.IgnoreCase),
            source);
    }

    [Fact]
    public void Seeder_RemainsDevelopmentOrTestOnly()
    {
        var source = ReadSeederSource();

        Assert.Contains("!_environment.IsDevelopment()", source, StringComparison.Ordinal);
        Assert.Contains("!_environment.IsEnvironment(\"Test\")", source, StringComparison.Ordinal);
        Assert.Contains("return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSource_DoesNotGrantBypassRls()
    {
        var sourceRoot = FindRepositoryPath("src");
        var grantPattern = new Regex(
            "ALTER\\s+ROLE[\\s\\S]{0,200}(?<!NO)BYPASSRLS",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => grantPattern.IsMatch(File.ReadAllText(path)))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string ReadSeederSource()
    {
        return File.ReadAllText(FindRepositoryPath(
            "src",
            "ONEVO.Infrastructure",
            "Persistence",
            "Seeders",
            "DevSmokeTestTenantSeeder.cs"));
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var repositoryMarker = Path.Combine(directory.FullName, "src", "ONEVO.Api");
            if (Directory.Exists(repositoryMarker))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
