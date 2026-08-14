using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.OrgStructure;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.OrgStructure.PositionTemplatePacks.Queries.ListPositionTemplatePacks;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for the tenant-facing Position Template Packs read endpoint
/// (GET /api/v1/org/position-template-packs): tenant policy, org:read gating, no tenantId
/// acceptance anywhere in the request surface, and strict IMediator-only constructor injection -
/// mirroring PositionsControllerArchitectureTests for the sibling PositionsController.
/// </summary>
public class PositionTemplatePacksControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(PositionTemplatePacksController);

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
    public void Controller_HasCorrectBaseRoute()
    {
        var routeAttr = ControllerType.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("api/v1/org/position-template-packs", routeAttr!.Template);
    }

    [Fact]
    public void ListAction_IsHttpGet_AndRequiresOrgReadPermission()
    {
        var listMethod = ControllerType.GetMethod(nameof(PositionTemplatePacksController.List));
        Assert.NotNull(listMethod);
        Assert.NotNull(listMethod!.GetCustomAttribute<HttpGetAttribute>());

        var attribute = listMethod.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("org:read", (string)field!.GetValue(attribute)!);
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
    }

    [Fact]
    public void NoAction_AcceptsTenantIdParameter()
    {
        var offenders = ControllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Query_HasNoTenantIdProperty()
    {
        var properties = typeof(ListPositionTemplatePacksQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name => string.Equals(name, "TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoDeleteOrMutatingEndpoint_Exists()
    {
        var methods = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        Assert.DoesNotContain(methods, m => m.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.DoesNotContain(methods, m => m.GetCustomAttribute<HttpPutAttribute>() is not null);
        Assert.DoesNotContain(methods, m => m.GetCustomAttribute<HttpDeleteAttribute>() is not null);
    }
}
