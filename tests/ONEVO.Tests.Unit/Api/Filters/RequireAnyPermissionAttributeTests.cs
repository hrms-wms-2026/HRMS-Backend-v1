using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Filters;

public class RequireAnyPermissionAttributeTests
{
    [Fact]
    public void AllowsLeaveReadOwn()
    {
        var context = Authorize(isAuthenticated: true, "leave:read-own");
        context.Result.Should().BeNull();
    }

    [Fact]
    public void AllowsLeaveManage()
    {
        var context = Authorize(isAuthenticated: true, "leave:manage");
        context.Result.Should().BeNull();
    }

    [Fact]
    public void RejectsAuthenticatedUserWithNeitherPermission()
    {
        var context = Authorize(isAuthenticated: true);
        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ReturnsUnauthorizedWhenUnauthenticated()
    {
        var context = Authorize(isAuthenticated: false);
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    private static AuthorizationFilterContext Authorize(bool isAuthenticated, params string[] held)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(isAuthenticated);
        currentUser.Setup(x => x.HasPermission(It.IsAny<string>()))
            .Returns((string permission) => held.Contains(permission));

        var services = new ServiceCollection();
        services.AddSingleton(currentUser.Object);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, []);
        new RequireAnyPermissionAttribute("leave:read-own", "leave:manage").OnAuthorization(context);
        return context;
    }
}
