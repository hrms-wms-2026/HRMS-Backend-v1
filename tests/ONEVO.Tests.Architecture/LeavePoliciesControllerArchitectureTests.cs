using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeavePoliciesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeavePoliciesController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Controller_HasCorrectBaseRoute()
    {
        var attr = ControllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("api/v1/leave/policies", attr!.Template);
    }

    [Fact]
    public void ReadActions_RequireLeaveRead()
    {
        Assert.Equal("leave:read", GetPermission(ControllerType, nameof(LeavePoliciesController.List)));
        Assert.Equal("leave:read", GetPermission(ControllerType, nameof(LeavePoliciesController.Get)));
    }

    [Theory]
    [InlineData(nameof(LeavePoliciesController.Create))]
    [InlineData(nameof(LeavePoliciesController.UpdateDetails))]
    [InlineData(nameof(LeavePoliciesController.AddVersion))]
    [InlineData(nameof(LeavePoliciesController.DeleteVersion))]
    [InlineData(nameof(LeavePoliciesController.SetAssignments))]
    [InlineData(nameof(LeavePoliciesController.Activate))]
    [InlineData(nameof(LeavePoliciesController.Deactivate))]
    [InlineData(nameof(LeavePoliciesController.Clone))]
    public void MutatingActions_RequireLeaveManage(string action) =>
        Assert.Equal("leave:manage", GetPermission(ControllerType, action));

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[]
                 {
                     typeof(CreateLeavePolicyRequest), typeof(CloneLeavePolicyRequest), typeof(AddLeavePolicyVersionRequest),
                     typeof(SetLeavePolicyAssignmentsRequest), typeof(UpdateLeavePolicyDetailsRequest),
                     typeof(SetEmployeeLeavePolicyRequest)
                 })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructor = Assert.Single(ControllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("IMediator", parameter.ParameterType.Name);
    }

    [Theory]
    [InlineData(nameof(EmployeeLeavePoliciesController.Set))]
    [InlineData(nameof(EmployeeLeavePoliciesController.Reset))]
    public void EmployeeOverrides_RequireLeaveManageOrCreate_AndVisibleEmployee(string action)
    {
        var method = typeof(EmployeeLeavePoliciesController).GetMethod(action)!;
        var any = method.GetCustomAttribute<RequireAnyPermissionAttribute>();
        Assert.NotNull(any);
        var permissions = (string[])typeof(RequireAnyPermissionAttribute)
            .GetField("_permissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(any)!;
        Assert.Equal(["leave:manage", "leave:create"], permissions);
        Assert.NotNull(method.GetCustomAttribute<RequireVisibleEmployeeAttribute>());
    }

    [Fact]
    public void EmployeePolicyList_RequiresVisibleEmployee() =>
        Assert.NotNull(typeof(EmployeeLeavePoliciesController)
            .GetMethod(nameof(EmployeeLeavePoliciesController.List))!
            .GetCustomAttribute<RequireVisibleEmployeeAttribute>());

    private static string GetPermission(Type controller, string methodName)
    {
        var method = controller.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
