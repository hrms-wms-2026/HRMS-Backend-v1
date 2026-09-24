using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeaveCalendarControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeaveCalendarController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Get_RequiresCalendarRead()
    {
        Assert.Equal("calendar:read", GetPermission(nameof(LeaveCalendarController.Get)));
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
