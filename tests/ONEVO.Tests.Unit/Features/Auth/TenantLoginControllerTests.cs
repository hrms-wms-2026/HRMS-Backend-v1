using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MediatR;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantLoginControllerTests
{
    [Fact]
    public async Task Login_OnTenantHost_ReturnsSafeRejection_AndNeverCallsMediator()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);

        var result = await controller.Login(
            new LoginRequest("owner@acme.test", "Password123!"),
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        var problemDetails = problem.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDetails.Detail.Should().Be("Tenant-host password login is not supported.");
        problemDetails.Detail.Should().NotContain("main login page");

        mediator.Verify(
            instance => instance.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-host password login must not reach MediatR at all - no command, no session, no side effect");

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().NotContain("onevo_session=");
        setCookie.Should().NotContain("onevo_mfa=");
    }

    private static AuthLoginController CreateController(IMediator mediator)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(instance => instance.ContextMode).Returns(TenantContextMode.Tenant);

        return new AuthLoginController(mediator, environment.Object, tenantContext.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
