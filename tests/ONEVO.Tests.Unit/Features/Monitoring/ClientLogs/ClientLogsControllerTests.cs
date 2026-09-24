using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Monitoring;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ClientLogs;

public class ClientLogsControllerTests
{
    [Fact]
    public void Controller_HasRoute()
    {
        var type = typeof(ClientLogsController);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/logs", routeAttr.Template);
    }

    [Fact]
    public void Create_RequiresAdminPolicy_WithNoPlatformPermission()
    {
        var method = typeof(ClientLogsController).GetMethod(nameof(ClientLogsController.Create));
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.Null(permissionAttr);
    }

    [Fact]
    public void Create_HasHttpPostAttribute_WithNoTemplate()
    {
        var method = typeof(ClientLogsController).GetMethod(nameof(ClientLogsController.Create));
        Assert.NotNull(method);

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Null(httpPost!.Template);
    }
}
