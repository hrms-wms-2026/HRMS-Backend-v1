using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Infrastructure.Identity.CurrentUser;
using FluentAssertions;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class CurrentPlatformUserContextTests
{
    private static IHttpContextAccessor BuildAccessor(ClaimsPrincipal? user)
    {
        var httpContext = new DefaultHttpContext();
        if (user is not null)
            httpContext.User = user;

        return new HttpContextAccessor { HttpContext = httpContext };
    }

    private static ClaimsPrincipal BuildPrincipal(string authenticationType, Guid userId, string? permission = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (permission is not null)
            claims.Add(new Claim(PlatformPermissionCatalog.PermissionClaimType, permission));

        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void UserId_AdminSchemeIdentity_ReturnsTheClaimValue()
    {
        var platformUserId = Guid.NewGuid();
        var accessor = BuildAccessor(BuildPrincipal("AdminScheme", platformUserId));

        var context = new CurrentPlatformUserContext(accessor);

        context.UserId.Should().Be(platformUserId);
    }

    [Fact]
    public void UserId_TenantSchemeIdentity_ReturnsNull()
    {
        // TenantDatabaseTicketStore stamps the exact same ClaimTypes.NameIdentifier claim
        // type on a tenant user's own session cookie. A scheme-blind read of that claim
        // would misread a plain tenant user's ID as a platform admin ID, incorrectly
        // triggering admin-only cross-tenant behavior for ordinary tenant requests.
        var tenantUserId = Guid.NewGuid();
        var accessor = BuildAccessor(BuildPrincipal("TenantScheme", tenantUserId));

        var context = new CurrentPlatformUserContext(accessor);

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void UserId_NoHttpContext_ReturnsNull()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };

        var context = new CurrentPlatformUserContext(accessor);

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void PlatformPermissions_TenantSchemeIdentity_ReturnsEmpty()
    {
        var accessor = BuildAccessor(BuildPrincipal("TenantScheme", Guid.NewGuid(), "employees:read"));

        var context = new CurrentPlatformUserContext(accessor);

        context.PlatformPermissions.Should().BeEmpty();
    }

    [Fact]
    public void PlatformPermissions_AdminSchemeIdentity_ReturnsTheClaims()
    {
        var accessor = BuildAccessor(BuildPrincipal("AdminScheme", Guid.NewGuid(), "platform.tenants.manage"));

        var context = new CurrentPlatformUserContext(accessor);

        context.PlatformPermissions.Should().Contain("platform.tenants.manage");
    }
}
