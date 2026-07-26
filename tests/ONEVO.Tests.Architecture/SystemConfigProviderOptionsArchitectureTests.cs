using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the screen-specific System Config provider option endpoints:
/// service-key-providers, oauth-providers, payment-gateway-providers.
/// These are read-only helper endpoints that let the UI select providers from
/// platform_providers metadata instead of the operator typing raw provider keys.
/// </summary>
public class SystemConfigProviderOptionsArchitectureTests
{
    private static readonly System.Type ControllerType =
        typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.SystemConfigProviderOptionsController);

    private static readonly System.Type DtoType =
        typeof(ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs.ProviderOptionDto);

    [Fact]
    public void Controller_IsUnderAdminDevPlatformSystemConfig()
    {
        Assert.Equal("ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_DoesNotInjectApplicationDbContext()
    {
        foreach (var ctor in ControllerType.GetConstructors())
        {
            foreach (var parameter in ctor.GetParameters())
            {
                Assert.NotEqual("ApplicationDbContext", parameter.ParameterType.Name);

                var ns = parameter.ParameterType.Namespace ?? string.Empty;
                Assert.False(
                    ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal),
                    $"{ControllerType.FullName} must not take an EF Core dependency: {parameter.ParameterType.FullName}");
            }
        }
    }

    [Fact]
    public void Controller_ExposesExactProviderOptionRoutes()
    {
        var routes = ControllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
            .Select(attribute => attribute.Template)
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "admin/v1/system-config/oauth-providers",
                "admin/v1/system-config/payment-gateway-providers",
                "admin/v1/system-config/service-key-providers"
            },
            routes);
    }

    [Fact]
    public void Controller_AllActions_RequireAdminPolicyAndSystemConfigReadPermission()
    {
        var authorizeAttribute = ControllerType.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal("AdminPolicy", authorizeAttribute!.Policy);

        var methods = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var platformAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePlatformPermissionAttribute");
            Assert.True(platformAttr is not null, $"{method.Name} must be protected by RequirePlatformPermission.");

            var tenantAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => a.GetType().Name == "RequirePermissionAttribute");
            Assert.True(tenantAttr is null, $"{method.Name} must not use the tenant RequirePermission attribute.");
        }
    }

    [Fact]
    public void ProviderOptionDto_HasNoSecretOrCredentialShapedProperties()
    {
        var forbiddenFragments = new[]
        {
            "secret", "credential", "token", "password", "apikey", "api_key",
            "privatekey", "private_key", "encrypted", "webhooksecret", "clientsecret",
            "credentialtarget", "providerfamily"
        };

        var propertyNames = DtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name.ToLowerInvariant())
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => forbiddenFragments.Any(fragment => name.Contains(fragment, System.StringComparison.Ordinal)));

        Assert.DoesNotContain(propertyNames, name => name == "id");
    }

    [Fact]
    public void SystemConfigFeatureSource_HasNoCredentialTargetField()
    {
        var featureRoot = FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform", "SystemConfig");
        Assert.True(Directory.Exists(featureRoot), $"Expected directory not found: {featureRoot}");

        var offenders = Directory
            .EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("credentialTarget", System.StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("credential_target", System.StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.Empty(offenders);
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
