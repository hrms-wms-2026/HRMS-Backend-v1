using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Contracts.Leave.Types;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveTypesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveTypesController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Actions_UseExpectedPermissions()
    {
        Assert.Equal("leave:read", GetPermission(nameof(LeaveTypesController.List)));
        Assert.Equal("leave:read", GetPermission(nameof(LeaveTypesController.Get)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Create)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Update)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveTypesController.Deactivate)));
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
        foreach (var contractType in new[] { typeof(CreateLeaveTypeRequest), typeof(UpdateLeaveTypeRequest) })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    // Code is immutable after create (spec §2.1: "Code cannot be changed after create") — the
    // mutable-fields contract must not even offer it, not just ignore it if sent.
    [Fact]
    public void UpdateLeaveTypeRequest_DoesNotExposeCode()
    {
        var names = typeof(UpdateLeaveTypeRequest).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(names, n => string.Equals(n, "Code", StringComparison.OrdinalIgnoreCase));
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
