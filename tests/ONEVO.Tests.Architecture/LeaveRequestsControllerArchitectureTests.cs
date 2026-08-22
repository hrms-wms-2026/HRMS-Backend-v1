using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Contracts.Leave.Requests;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveRequestsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveRequestsController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Actions_UseExpectedPermissions()
    {
        Assert.Equal("leave:read-own", GetPermission(nameof(LeaveRequestsController.Submit)));
        Assert.Equal("leave:read-own", GetPermission(nameof(LeaveRequestsController.Preview)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveRequestsController.SubmitOnBehalf)));
        Assert.Equal("leave:read-own", GetPermission(nameof(LeaveRequestsController.ListMine)));
        Assert.Equal(["leave:read-own", "leave:manage"], GetAnyPermissions(nameof(LeaveRequestsController.Cancel)));
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructor = Assert.Single(ControllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("IMediator", parameter.ParameterType.Name);
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[] { typeof(SubmitLeaveRequestRequest), typeof(SubmitLeaveRequestOnBehalfRequest), typeof(CancelLeaveRequestRequest) })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
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

    private static string[] GetAnyPermissions(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequireAnyPermissionAttribute>();
        Assert.NotNull(attribute);
        var field = typeof(RequireAnyPermissionAttribute)
            .GetField("_permissions", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string[])field!.GetValue(attribute)!;
    }
}
