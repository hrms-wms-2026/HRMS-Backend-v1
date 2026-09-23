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
        Assert.Equal("leave:read", GetPermission(nameof(LeavePoliciesController.List)));
        Assert.Equal("leave:read", GetPermission(nameof(LeavePoliciesController.Get)));
    }

    [Fact]
    public void MutatingActions_RequireLeaveManage()
    {
        Assert.Equal("leave:manage", GetPermission(nameof(LeavePoliciesController.Create)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeavePoliciesController.Clone)));
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[] { typeof(CreateLeavePolicyRequest), typeof(CloneLeavePolicyRequest) })
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

    private static string GetPermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
