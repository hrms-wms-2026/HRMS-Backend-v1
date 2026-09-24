using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture tests for PositionsController in Part 2C:
/// Verifies controller location, tenant policy, exact base route, permission attributes,
/// route-scoped legalEntityId, absence of tenantId/role/HeadPositionId exposure, no DELETE
/// endpoint, no dead Archive/Restore request contracts, and strict constructor injection
/// (IMediator only, no DbContext/repository/ICurrentUser/tenant service).
/// </summary>
public class PositionsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(PositionsController);

    [Fact]
    public void Controller_ExistsIn_TenantOrgStructureNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.OrgStructure", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var authorizeAttribute = ControllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("TenantPolicy", authorizeAttribute!.Policy);
    }

    [Fact]
    public void Controller_HasCorrectBaseRoute_WithLegalEntityIdGuidConstraint()
    {
        var routeAttr = ControllerType.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("api/v1/org/legal-entities/{legalEntityId:guid}/positions", routeAttr!.Template);
    }

    private static IEnumerable<MethodInfo> ActionMethods()
        => ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    [Fact]
    public void AllEightRequiredRoutesAndVerbs_Exist()
    {
        var listMethod = ControllerType.GetMethod(nameof(PositionsController.List));
        var getMethod = ControllerType.GetMethod(nameof(PositionsController.Get));
        var treeMethod = ControllerType.GetMethod(nameof(PositionsController.Tree));
        var createMethod = ControllerType.GetMethod(nameof(PositionsController.Create));
        var updateMethod = ControllerType.GetMethod(nameof(PositionsController.Update));
        var archiveCheckMethod = ControllerType.GetMethod(nameof(PositionsController.ArchiveCheck));
        var archiveMethod = ControllerType.GetMethod(nameof(PositionsController.Archive));
        var restoreMethod = ControllerType.GetMethod(nameof(PositionsController.Restore));

        Assert.NotNull(listMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(listMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(getMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("{positionId:guid}", getMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(treeMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("tree", treeMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(createMethod?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(createMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.NotNull(updateMethod?.GetCustomAttribute<HttpPutAttribute>());
        Assert.Equal("{positionId:guid}", updateMethod!.GetCustomAttribute<HttpPutAttribute>()!.Template);

        Assert.NotNull(archiveCheckMethod?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{positionId:guid}/archive-check", archiveCheckMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.NotNull(archiveMethod?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{positionId:guid}/archive", archiveMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.NotNull(restoreMethod?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("{positionId:guid}/restore", restoreMethod!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void ReadActions_RequireOrgReadPermission()
    {
        var listMethod = ControllerType.GetMethod(nameof(PositionsController.List));
        var getMethod = ControllerType.GetMethod(nameof(PositionsController.Get));
        var treeMethod = ControllerType.GetMethod(nameof(PositionsController.Tree));
        var archiveCheckMethod = ControllerType.GetMethod(nameof(PositionsController.ArchiveCheck));

        Assert.Equal("org:read", GetPermission(listMethod!));
        Assert.Equal("org:read", GetPermission(getMethod!));
        Assert.Equal("org:read", GetPermission(treeMethod!));
        Assert.Equal("org:read", GetPermission(archiveCheckMethod!));
    }

    [Theory]
    [InlineData(nameof(PositionsController.Create))]
    [InlineData(nameof(PositionsController.Update))]
    [InlineData(nameof(PositionsController.Archive))]
    [InlineData(nameof(PositionsController.Restore))]
    public void MutatingActions_RequireOrgManagePermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        Assert.Equal("org:manage", GetPermission(method!));
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructors = ControllerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal("IMediator", parameters[0].ParameterType.Name);
    }

    [Fact]
    public void Controller_DoesNotInjectDbContext_Repositories_OrUserServicesDirectly()
    {
        var fields = ControllerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var fieldTypeNames = fields.Select(f => f.FieldType.Name).ToList();

        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("DbContext", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("Repository", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fieldTypeNames, name => name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControllerSource_ContainsNoDbContextRepositoryOrCurrentUserUsage()
    {
        var path = FindFileUnderRepoRoot(
            "src", "ONEVO.Api", "Controllers", "Tenant", "OrgStructure", "PositionsController.cs");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("DbContext", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Repository", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ICurrentUser", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ITenant", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(typeof(CreatePositionRequest))]
    [InlineData(typeof(UpdatePositionRequest))]
    public void RequestContracts_DoNotExposeTenantId_LegalEntityId_OrHeadPositionId(Type contractType)
    {
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => string.Equals(name, "LegalEntityId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => string.Equals(name, "HeadPositionId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(typeof(CreatePositionRequest))]
    [InlineData(typeof(UpdatePositionRequest))]
    public void RequestContracts_DoNotExposeRoleOrPermissionCreationFields(Type contractType)
    {
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        string[] forbidden = ["Role", "Permission", "AccessRole", "DefaultRoleId"];
        Assert.DoesNotContain(properties, name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void NoDeletePositionEndpoint_Exists()
    {
        var offenders = ActionMethods()
            .Where(m => m.GetCustomAttribute<HttpDeleteAttribute>() is not null)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoDeadArchiveOrRestorePositionRequestContracts_Exist()
    {
        var assembly = typeof(CreatePositionRequest).Assembly;

        Assert.Null(assembly.GetType("ONEVO.Api.Contracts.OrgStructure.Positions.ArchivePositionRequest"));
        Assert.Null(assembly.GetType("ONEVO.Api.Contracts.OrgStructure.Positions.RestorePositionRequest"));
    }

    [Fact]
    public void NoEndpoint_AcceptsOrMutatesHeadPositionId()
    {
        var offenders = ActionMethods()
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "headPositionId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);

        var path = FindFileUnderRepoRoot(
            "src", "ONEVO.Api", "Controllers", "Tenant", "OrgStructure", "PositionsController.cs");
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("HeadPositionId", text, StringComparison.Ordinal);
    }

    private static string GetPermission(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }

    private static string FindFileUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
