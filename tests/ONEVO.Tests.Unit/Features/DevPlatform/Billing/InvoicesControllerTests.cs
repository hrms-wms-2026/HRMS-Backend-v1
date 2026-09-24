using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.Billing;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class InvoicesControllerTests
{
    [Fact]
    public void Controller_HasRoute()
    {
        var routeAttr = typeof(InvoicesController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/invoices", routeAttr!.Template);
    }

    [Theory]
    [InlineData(nameof(InvoicesController.List), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(InvoicesController.GetById), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(InvoicesController.ListByTenant), PlatformPermissionCatalog.SubscriptionsRead)]
    [InlineData(nameof(InvoicesController.Create), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(InvoicesController.MarkPaid), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(InvoicesController.Void), PlatformPermissionCatalog.SubscriptionsManage)]
    [InlineData(nameof(InvoicesController.ResendEmail), PlatformPermissionCatalog.SubscriptionsManage)]
    public void Endpoint_RequiresCorrectPlatformPermission(string methodName, string expectedPermission)
    {
        var method = typeof(InvoicesController).GetMethod(methodName);
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr!.Policy);

        var attr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr!.Permission);
    }
}
