using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the Developer Platform / System Config / Platform OAuth Apps feature.
/// - Application handlers must not touch EF Core or ApplicationDbContext (repository interfaces only).
/// - The controller must live under Admin/DevPlatform/SystemConfig and use platform permissions only.
/// - No controller may depend on the resolver that returns decrypted secrets.
/// - No forbidden later-step entity (tenant tokens, integration catalog, AI configs, Slack) is introduced.
/// </summary>
public class PlatformOAuthAppsArchitectureTests
{
    private const string FeatureNamespace =
        "ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps";

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    private static readonly Assembly DomainAssembly =
        typeof(ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities.PlatformOAuthApp).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformOAuthAppsController).Assembly;

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
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformOAuthAppsController);
        Assert.Equal("ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig", controllerType.Namespace);
    }

    [Fact]
    public void Controller_UsesPlatformPermissions_NotTenantRequirePermission()
    {
        var controllerType = typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformOAuthAppsController);
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var tenantAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePermissionAttribute");
            Assert.True(tenantAttr is null,
                $"{method.Name} must not use the tenant RequirePermission attribute.");

            var platformAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePlatformPermissionAttribute");
            Assert.True(platformAttr is not null,
                $"{method.Name} must be protected by RequirePlatformPermission.");
        }
    }

    [Fact]
    public void NoController_DependsOnPlatformOAuthAppResolver()
    {
        // IPlatformOAuthAppResolver returns decrypted secrets for server-side use only.
        // No controller may take it as a dependency, so no endpoint can leak a decrypted secret.
        var controllerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)))
            .ToList();
        Assert.NotEmpty(controllerTypes);

        foreach (var controller in controllerTypes)
        {
            foreach (var ctor in controller.GetConstructors())
            {
                var offender = ctor.GetParameters()
                    .FirstOrDefault(p => p.ParameterType.Name == "IPlatformOAuthAppResolver");
                Assert.True(offender is null,
                    $"{controller.FullName} must not depend on IPlatformOAuthAppResolver.");
            }
        }
    }

    [Fact]
    public void EfPlatformOAuthAppRepository_HasNoExpressionBodiedMembers()
    {
        var repositoryPath = FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories",
            "DevPlatform", "SystemConfig", "EfPlatformOAuthAppRepository.cs");
        var sourceLines = File.ReadAllLines(repositoryPath);

        var expressionBodiedMembers = sourceLines
            .Select((line, index) => new
            {
                LineNumber = index + 1,
                Text = line.Trim()
            })
            .Where(line =>
                line.Text.StartsWith("=>", StringComparison.Ordinal) ||
                (line.Text.StartsWith("public ", StringComparison.Ordinal) &&
                 line.Text.Contains("=>", StringComparison.Ordinal)))
            .Select(line => $"{repositoryPath}:{line.LineNumber}: {line.Text}")
            .ToArray();

        Assert.Empty(expressionBodiedMembers);
    }

    [Fact]
    public void NoForbiddenLaterStepEntity_IsIntroduced()
    {
        // Provider-specific calendar, email, AI, and Slack entities remain later-step work.
        var forbiddenTypeNames = new[]
        {
            "ExternalCalendarConnection",
            "EmailCredential",
            "AiProviderConfig",
            "TenantAiProviderOverride",
            "SlackApp",
            "SlackIntegration",
            "SlackWorkspace"
        };

        var domainTypeNames = DomainAssembly.GetTypes().Select(t => t.Name).ToHashSet();
        foreach (var forbidden in forbiddenTypeNames)
            Assert.False(domainTypeNames.Contains(forbidden),
                $"Forbidden later-step entity '{forbidden}' must not be introduced in this step.");
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "ONEVO.Api")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
