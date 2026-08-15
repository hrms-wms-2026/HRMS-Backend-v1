using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Billing;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class TenantSubscriptionsControllerTests
{
    [Fact]
    public void Controller_HasRoute()
    {
        var routeAttr = typeof(TenantSubscriptionsController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/tenants/{tenantId:guid}/subscription", routeAttr!.Template);
    }

    [Fact]
    public void Get_RequiresSubscriptionsReadPermission()
    {
        var method = typeof(TenantSubscriptionsController).GetMethod(nameof(TenantSubscriptionsController.Get));
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(permissionAttr);
        Assert.Equal(PlatformPermissionCatalog.SubscriptionsRead, permissionAttr!.Permission);

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
    }
}
