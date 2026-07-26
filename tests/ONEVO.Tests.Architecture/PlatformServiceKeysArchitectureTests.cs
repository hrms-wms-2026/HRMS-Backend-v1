using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the Developer Platform / System Config / Platform Service Keys feature.
/// - Application handlers must not touch EF Core or ApplicationDbContext (repository interfaces only).
/// - The controller must live under Admin/DevPlatform/SystemConfig.
/// - No controller may depend on the runtime resolver that returns decrypted keys.
/// </summary>
public class PlatformServiceKeysArchitectureTests
{
    private const string FeatureNamespace =
        "ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys";

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformServiceKeysController).Assembly;

    [Fact]
    public void ApplicationFeature_DoesNotReference_EfCoreOrApplicationDbContext()
    {
        // The Application assembly as a whole must not reference EF Core at all.
        var efReferences = ApplicationAssembly.GetReferencedAssemblies()
            .Where(a => a.Name != null && a.Name.StartsWith("Microsoft.EntityFrameworkCore"))
            .ToList();
        Assert.Empty(efReferences.Select(a => a.Name));

        // And no type in the feature namespace may take ApplicationDbContext or any EF type.
        var featureTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(FeatureNamespace))
            .ToList();
        Assert.NotEmpty(featureTypes);

        foreach (var type in featureTypes)
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var ns = param.ParameterType.Namespace ?? string.Empty;
                    Assert.False(ns.StartsWith("Microsoft.EntityFrameworkCore"),
                        $"{type.FullName} takes an EF Core dependency: {param.ParameterType.FullName}");
                    Assert.NotEqual("ApplicationDbContext", param.ParameterType.Name);
                }
            }
        }
    }

    [Fact]
    public void Controller_IsUnderAdminDevPlatformSystemConfig()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformServiceKeysController);
        Assert.Equal("ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig", controllerType.Namespace);
    }

    [Fact]
    public void NoController_DependsOnPlatformServiceKeyResolver()
    {
        // IPlatformServiceKeyResolver returns decrypted keys for server-side use only.
        // No controller may take it as a dependency, so no endpoint can leak a decrypted key.
        var controllerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)))
            .ToList();
        Assert.NotEmpty(controllerTypes);

        foreach (var controller in controllerTypes)
        {
            foreach (var ctor in controller.GetConstructors())
            {
                var offender = ctor.GetParameters()
                    .FirstOrDefault(p => p.ParameterType.Name == "IPlatformServiceKeyResolver");
                Assert.True(offender is null,
                    $"{controller.FullName} must not depend on IPlatformServiceKeyResolver.");
            }
        }
    }

    // Scoped readability guard for the Phase 1C repository cleanup.
    // Only inspects EfPlatformServiceKeyRepository.cs; other repositories are not covered yet.
    [Fact]
    public void EfPlatformServiceKeyRepository_HasNoExpressionBodiedMembers()
    {
        var repositoryPath = FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories", "DevPlatform", "SystemConfig",
            "EfPlatformServiceKeyRepository.cs");

        Assert.True(File.Exists(repositoryPath), $"Expected file not found: {repositoryPath}");

        var source = File.ReadAllText(repositoryPath);

        var expressionBodiedMemberPattern = new Regex(@"^\s*(public|private|protected|internal)[^{;]*\)\s*=>", RegexOptions.Multiline);
        var match = expressionBodiedMemberPattern.Match(source);

        Assert.True(match.Success == false,
            $"EfPlatformServiceKeyRepository.cs must not contain expression-bodied members. Offending text: '{match.Value.Trim()}'");
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
