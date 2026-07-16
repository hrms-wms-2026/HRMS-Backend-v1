using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class RequirePlatformPermissionAttributeTests
{
    private static AuthorizationFilterContext BuildContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    private static ClaimsPrincipal AuthenticatedUser(params string[] permissionCodes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("platform_role", "Some Role")
        };
        foreach (var code in permissionCodes)
            claims.Add(new Claim(PlatformPermissionCatalog.PermissionClaimType, code));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
    }

    [Fact]
    public void UserWithPermission_IsAllowed()
    {
        var context = BuildContext(AuthenticatedUser(PlatformPermissionCatalog.TenantsRead));
        var sut = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.TenantsRead);

        sut.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void UserWithoutPermission_Gets403WithContractBody()
    {
        var context = BuildContext(AuthenticatedUser(PlatformPermissionCatalog.TenantsRead));
        var sut = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.TenantsManage);

        sut.OnAuthorization(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        json.Should().Contain("permission_denied");
        json.Should().Contain(PlatformPermissionCatalog.TenantsManage);
    }

    [Fact]
    public void RoleNameIsNeverEnoughWithoutPermissionClaim()
    {
        // A platform_role claim alone must not authorize anything —
        // role names are never authorization rules.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("platform_role", "Platform Super Admin")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
        var context = BuildContext(user);
        var sut = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.TenantsRead);

        sut.OnAuthorization(context);

        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void UnauthenticatedUser_IsLeftToAuthorizeAttribute()
    {
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()));
        var sut = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.TenantsRead);

        sut.OnAuthorization(context);

        // No 403 shortcut — the cookie scheme / [Authorize] returns the 401.
        context.Result.Should().BeNull();
    }
}
