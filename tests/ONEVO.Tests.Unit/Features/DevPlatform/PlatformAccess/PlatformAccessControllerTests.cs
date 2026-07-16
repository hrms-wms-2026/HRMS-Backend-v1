using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Controllers.Admin.DevPlatform.PlatformAccess;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class PlatformAccessControllerTests
{
    [Fact]
    public void Controller_HasAdminPolicy_AndRoute()
    {
        var type = typeof(PlatformAccessController);
        
        var authAttr = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal("AdminPolicy", authAttr.Policy);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("admin/v1/platform-access", routeAttr.Template);
    }

    [Theory]
    [InlineData(nameof(PlatformAccessController.ListUsers), PlatformPermissionCatalog.AccountsRead)]
    [InlineData(nameof(PlatformAccessController.GetUserDetail), PlatformPermissionCatalog.AccountsRead)]
    [InlineData(nameof(PlatformAccessController.ListRoles), PlatformPermissionCatalog.RolesRead)]
    [InlineData(nameof(PlatformAccessController.GetRoleDetail), PlatformPermissionCatalog.RolesRead)]
    [InlineData(nameof(PlatformAccessController.ListPermissions), PlatformPermissionCatalog.RolesRead)]
    [InlineData(nameof(PlatformAccessController.UpdateRolePermissions), PlatformPermissionCatalog.RolesManage)]
    [InlineData(nameof(PlatformAccessController.UpdateUserRoles), PlatformPermissionCatalog.AccountsManage)]
    [InlineData(nameof(PlatformAccessController.ListUserSessions), PlatformPermissionCatalog.SecurityRead)]
    [InlineData(nameof(PlatformAccessController.RevokeUserSession), PlatformPermissionCatalog.SecurityManage)]
    [InlineData(nameof(PlatformAccessController.RevokeAllUserSessions), PlatformPermissionCatalog.SecurityManage)]
    [InlineData(nameof(PlatformAccessController.ListAuthEvents), PlatformPermissionCatalog.AuditRead)]
    public void Endpoint_RequiresCorrectPlatformPermission(string methodName, string expectedPermission)
    {
        var method = typeof(PlatformAccessController).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr.Permission);
    }
}
