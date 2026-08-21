using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Entitlements;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveEntitlementsControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveEntitlementsController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void MutatingActions_RequireLeaveManage()
    {
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.PreviewGenerate)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Generate)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.CreateManual)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Adjust)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Recalculate)));
    }

    [Fact]
    public void List_RequiresLeaveRead()
    {
        Assert.Equal("leave:read", GetPermission(nameof(LeaveEntitlementsController.List)));
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
        foreach (var contractType in new[]
                 {
                     typeof(GenerateEntitlementsRequest),
                     typeof(CreateManualEntitlementRequest),
                     typeof(AdjustEntitlementRequest),
                     typeof(RecalculateEntitlementRequest)
                 })
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
}
