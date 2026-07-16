using System.Linq;
using System.Reflection;
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
}
