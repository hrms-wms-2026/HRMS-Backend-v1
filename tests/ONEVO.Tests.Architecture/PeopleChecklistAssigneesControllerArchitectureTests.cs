using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.People;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class PeopleChecklistAssigneesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(PeopleChecklistAssigneesController);

    [Fact]
    public void Controller_ExistsIn_TenantCoreHrNamespace()
    {
        Assert.Equal("ONEVO.Api.Controllers.Tenant.CoreHr", ControllerType.Namespace);
    }

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        Assert.Equal("TenantPolicy", ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void Controller_HasPeopleBaseRoute()
    {
        Assert.Equal("api/v1/people", ControllerType.GetCustomAttribute<RouteAttribute>()?.Template);
    }

    /// <summary>The pickers list people by name, so they stay limited to the two screens that use
    /// them: Add Employee (employees:write) and the checklist template builder (checklists:manage).</summary>
    [Theory]
    [InlineData(nameof(PeopleChecklistAssigneesController.List), "checklist-assignees")]
    [InlineData(nameof(PeopleChecklistAssigneesController.ListPositions), "checklist-assignee-positions")]
    public void Pickers_RequireEmployeesWriteOrChecklistsManage(string methodName, string route)
    {
        var method = ControllerType.GetMethod(methodName);
        Assert.Equal(route, method!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.Null(method.GetCustomAttribute<RequirePermissionAttribute>());
        var permission = method.GetCustomAttribute<RequireAnyPermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal(new[] { "employees:write", "checklists:manage" }, permission!.Permissions);
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
    public void NoAction_AcceptsTenantIdParameter()
    {
        var offenders = ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .SelectMany(m => m.GetParameters())
            .Where(p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(offenders);
    }

    [Fact]
    public void ViewModel_IncludesUserId_AndOmitsTenantId()
    {
        var names = typeof(ChecklistAssigneeViewModel).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("UserId", names);
        Assert.Contains("EmployeeId", names);
        Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
    }
}
