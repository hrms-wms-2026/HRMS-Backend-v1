using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure;
using ONEVO.Infrastructure.Identity.Mfa;

namespace ONEVO.Tests.Architecture;

public sealed class MfaChallengeStorageArchitectureTests
{
    [Fact]
    public void InfrastructureRegistration_UsesPostgresBackedMfaChallengeStoreAsNormalRuntime()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=onevo_arch_test;Username=onevo;Password=onevo"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMfaChallengeStore));

        descriptor.Should().NotBeNull("IMfaChallengeStore must be registered by AddInfrastructure");
        descriptor!.ImplementationType.Should().Be(
            typeof(PostgresMfaChallengeStore),
            "the normal runtime MFA challenge store must be the PostgreSQL-backed mfa_challenges implementation");
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData("MemoryMfaChallengeStore")]
    [InlineData("HttpContextTenantContext")]
    [InlineData("MfaChallengeStoreStartupGuard")]
    [InlineData("AllowProcessLocalChallengeStore")]
    public void ProductionSource_ContainsNoRemovedIdentityFallback(string removedTypeOrOption)
    {
        var offenders = ScanProductionSourceFor(removedTypeOrOption);

        offenders.Should().BeEmpty(
            $"{removedTypeOrOption} was removed and must not return to production source");
    }

    [Fact]
    public void InfrastructureIdentityFolder_ExistsAndContainsNoLooseSourceFiles()
    {
        var identityDirectory = Path.Combine(
            FindProductionSourceRoot(),
            "ONEVO.Infrastructure",
            "Identity");

        Directory.Exists(identityDirectory).Should().BeTrue(
            "Infrastructure/Identity is the canonical parent for identity implementations");
        Directory.GetFiles(identityDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Should()
            .BeEmpty("identity implementations must be organized into responsibility subfolders");
    }

    [Theory]
    [InlineData("Auth")]
    [InlineData("Tenancy")]
    [InlineData("Time")]
    public void Infrastructure_ContainsNoUnapprovedTopLevelIdentityOwnershipFolder(
        string folderName)
    {
        var directory = Path.Combine(
            FindProductionSourceRoot(),
            "ONEVO.Infrastructure",
            folderName);

        Directory.Exists(directory).Should().BeFalse(
            $"{folderName} is owned by Infrastructure/Identity for this cleanup");
    }

    [Fact]
    public void InfrastructureIdentityTenantContexts_ContainNoNotImplementedException()
    {
        var tenantContextDirectory = Path.Combine(
            FindProductionSourceRoot(),
            "ONEVO.Infrastructure",
            "Identity",
            "Tenancy");
        var offenders = Directory.EnumerateFiles(
                tenantContextDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "NotImplementedException",
                StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void ProductionSource_ContainsNoSqliteReferences()
    {
        // The backend runtime is PostgreSQL/Npgsql only. SQLite is permitted exclusively inside
        // test projects; production source must never carry provider branches or workarounds.
        var offenders = ScanProductionSourceFor("sqlite");

        offenders.Should().BeEmpty(
            "production source under 'src' must contain no SQLite provider references");
    }

    [Fact]
    public void ProductionSource_ContainsNoDateTimeOffsetToBinaryConverter()
    {
        // DateTimeOffsetToBinaryConverter exists only to make SQLite translate DateTimeOffset
        // comparisons; it must never be applied to the production (PostgreSQL) model.
        var offenders = ScanProductionSourceFor("DateTimeOffsetToBinaryConverter");

        offenders.Should().BeEmpty(
            "production source under 'src' must not use DateTimeOffsetToBinaryConverter");
    }

    private static IReadOnlyList<string> ScanProductionSourceFor(string token)
    {
        var sourceRoot = FindProductionSourceRoot();
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(binSegment, StringComparison.OrdinalIgnoreCase)
                && !f.Contains(objSegment, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string FindProductionSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ONEVO.Infrastructure");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "src");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository 'src' directory walking up from {AppContext.BaseDirectory}");
    }
}
