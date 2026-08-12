using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture tests for WorkModesController: route, tenant policy (401 for unauthenticated -
/// enforced by ASP.NET's [Authorize] pipeline, not directly unit-testable without a live host),
/// and the employees:write permission gate (403 - enforced by RequirePermissionAttribute's
/// authorization filter, likewise not directly unit-testable without a live host). Both are
/// verified here by reflection instead, matching the existing convention in
/// DepartmentsControllerArchitectureTests.
/// </summary>
public class WorkModesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(WorkModesController);

    [Fact]
    public void Controller_ExistsIn_TenantCoreHrNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.CoreHr", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var authorizeAttribute = ControllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("TenantPolicy", authorizeAttribute!.Policy);
    }

    [Fact]
    public void Controller_HasWorkModesRoute()
    {
        var routeAttr = ControllerType.GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("api/v1/work-modes", routeAttr!.Template);
    }

    [Fact]
    public void ListAction_IsHttpGet_WithNoTemplate()
    {
        var listMethod = ControllerType.GetMethod(nameof(WorkModesController.List));

        Assert.NotNull(listMethod?.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(listMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void ListAction_RequiresEmployeesWritePermission()
    {
        var listMethod = ControllerType.GetMethod(nameof(WorkModesController.List));
        var attribute = listMethod!.GetCustomAttribute<RequirePermissionAttribute>();

        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }

    [Fact]
    public void ListAction_AcceptsNoTenantIdParameter()
    {
        var listMethod = ControllerType.GetMethod(nameof(WorkModesController.List));
        var offenders = listMethod!.GetParameters()
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(offenders);
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
}
