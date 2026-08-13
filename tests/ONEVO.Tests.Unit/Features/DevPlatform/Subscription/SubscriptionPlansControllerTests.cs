using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Subscriptions;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Subscription;

public class SubscriptionPlansControllerTests
{
    [Fact]
    public void Controller_HasRoute()
    {
        var type = typeof(SubscriptionPlansController);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/subscription-plans", routeAttr.Template);
    }

    [Theory]
    [InlineData(nameof(SubscriptionPlansController.List), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(SubscriptionPlansController.GetById), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(SubscriptionPlansController.Create), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Update), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(SubscriptionPlansController.Archive), PlatformPermissionCatalog.SubscriptionsManage)]
    public void Endpoint_RequiresCorrectPlatformPermission(string methodName, string expectedPermission)
    {
        var method = typeof(SubscriptionPlansController).GetMethod(methodName);
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var attr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr!.Permission);
    }

    [Fact]
    public void List_HasHttpGetAttribute_WithNoTemplate()
    {
        var method = typeof(SubscriptionPlansController).GetMethod(nameof(SubscriptionPlansController.List));
        Assert.NotNull(method);

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Null(httpGet!.Template);
    }

    [Fact]
    public void GetById_HasHttpGetAttribute_WithIdTemplate()
    {
        var method = typeof(SubscriptionPlansController).GetMethod(nameof(SubscriptionPlansController.GetById));
        Assert.NotNull(method);

        var httpGet = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("{id:guid}", httpGet!.Template);
    }
}
