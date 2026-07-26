using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the Developer Platform Configuration Template Manager
/// foundation (configuration_templates / tenant_configuration_template_applications).
/// </summary>
public class ConfigurationTemplateManagerArchitectureTests
{
    private const string FeatureNamespace =
        "ONEVO.Application.Features.DevPlatform.ConfigurationTemplates";

    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    [Fact]
    public void ApplicationFeature_DoesNotReference_EfCoreOrApplicationDbContext()
    {
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
    public void AdminConfigurationTemplatesController_UsesPlatformPermissions_NotTenantRequirePermission()
    {
        AssertControllerUsesPlatformPermissionsOnly(
            typeof(ONEVO.Api.Controllers.Admin.DevPlatform.ConfigurationTemplates.AdminConfigurationTemplatesController));
    }

    [Fact]
    public void AdminTenantConfigurationTemplatesController_UsesPlatformPermissions_NotTenantRequirePermission()
    {
        AssertControllerUsesPlatformPermissionsOnly(
            typeof(ONEVO.Api.Controllers.Admin.DevPlatform.Tenants.AdminTenantConfigurationTemplatesController));
    }

    [Fact]
    public void EfConfigurationTemplateRepository_HasNoExpressionBodiedMembers()
    {
        AssertNoExpressionBodiedMembers(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories",
            "DevPlatform", "ConfigurationTemplates", "EfConfigurationTemplateRepository.cs"));
    }

    [Fact]
    public void EfTenantConfigurationTemplateApplicationRepository_HasNoExpressionBodiedMembers()
    {
        AssertNoExpressionBodiedMembers(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Persistence", "Repositories",
            "DevPlatform", "ConfigurationTemplates", "EfTenantConfigurationTemplateApplicationRepository.cs"));
    }

    [Fact]
    public void Migration_CreatesOnlyTheTwoCanonicalConfigurationTemplateTables()
    {
        var migrationsDir = FindRepositoryPath("src", "ONEVO.Infrastructure", "Migrations");
        var migrationFile = Directory.EnumerateFiles(migrationsDir, "*_AddConfigurationTemplates.cs")
            .FirstOrDefault(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        Assert.True(migrationFile is not null,
            "Expected a migration file matching *_AddConfigurationTemplates.cs under src/ONEVO.Infrastructure/Migrations.");

        var text = File.ReadAllText(migrationFile!);
        var createTableCount = System.Text.RegularExpressions.Regex.Matches(text, "migrationBuilder\\.CreateTable").Count;
        Assert.Equal(2, createTableCount);

        Assert.Contains("\"configuration_templates\"", text);
        Assert.Contains("\"tenant_configuration_template_applications\"", text);

        var forbidden = new[]
        {
            "\"setup_services\"",
            "\"tenant_setup_services\"",
            "\"tenant_setup_selections\"",
            "\"setup_options\"",
            "\"tenant_one_time_charges\""
        };
        foreach (var term in forbidden)
        {
            Assert.DoesNotContain(term, text);
        }
    }

    [Fact]
    public void SourceTree_ConfigurationTemplateFeature_DoesNotReintroduceRetiredSetupServiceIdentifiers()
    {
        var srcDir = FindRepositoryPath("src");
        var configTemplateFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}ConfigurationTemplates{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var banned = new[]
        {
            "TenantSetupOptionKeys",
            "ITenantSetupSelectionRepository",
            "EfTenantSetupSelectionRepository",
            "TenantOneTimeCharge",
            "setup_services",
            "tenant_setup_services"
        };

        var offenders = new List<string>();
        foreach (var file in configTemplateFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var term in banned)
            {
                if (text.Contains(term, StringComparison.Ordinal))
                {
                    offenders.Add($"{file}: {term}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Retired setup-service identifiers found in Configuration Template Manager source: " + string.Join("; ", offenders));
    }

    private static void AssertControllerUsesPlatformPermissionsOnly(Type controllerType)
    {
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

    private static void AssertNoExpressionBodiedMembers(string repositoryPath)
    {
        var sourceLines = File.ReadAllLines(repositoryPath);
        var expressionBodiedMembers = sourceLines
            .Select((line, index) => new { LineNumber = index + 1, Text = line.Trim() })
            .Where(line =>
                line.Text.StartsWith("=>", StringComparison.Ordinal) ||
                (line.Text.StartsWith("public ", StringComparison.Ordinal) &&
                 line.Text.Contains("=>", StringComparison.Ordinal)))
            .Select(line => $"{repositoryPath}:{line.LineNumber}: {line.Text}")
            .ToArray();

        Assert.Empty(expressionBodiedMembers);
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
