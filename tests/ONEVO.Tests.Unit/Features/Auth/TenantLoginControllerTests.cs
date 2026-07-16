using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.Commands.Login;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using System.Text.Json;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantLoginControllerTests
{
    [Fact]
    public async Task Login_WithStaleInvalidSessionCookie_ClearsCookiesAndContinuesLogin()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Failure("Invalid email or password.", 401));

        var controller = CreateController(mediator.Object);
        controller.Request.Headers.Cookie =
            "onevo_session=stale-invalid-ticket; onevo_csrf=stale-csrf";

        var result = await controller.Login(
            new AuthController.LoginRequest("owner@acme.test", "Password123!"),
            CancellationToken.None);

        result.Should().BeOfType<ObjectResult>();
        mediator.Verify(
            instance => instance.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("onevo_session=");
        setCookie.Should().Contain("onevo_csrf=");
    }

    [Fact]
    public async Task Login_WhenMfaIsRequired_StoresChallengeInHttpOnlyCookieOnly()
    {
        const string mfaChallenge = "opaque-mfa-challenge";
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto(
                CsrfToken: string.Empty,
                CsrfTokenHash: string.Empty,
                ExpiresAt: null,
                RequiresMfa: true,
                MfaChallenge: mfaChallenge)));

        var controller = CreateController(mediator.Object);

        var result = await controller.Login(
            new AuthController.LoginRequest("owner@acme.test", "Password123!"),
            CancellationToken.None);

        var response = result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.Value.Should().BeOfType<AuthSessionResponseDto>();

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("onevo_mfa=");
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("path=/api/v1/auth");
        setCookie.Should().NotContain("header.payload.signature");
        JsonSerializer.Serialize(response.Value).Should().NotContain(mfaChallenge);
    }

    private static AuthController CreateController(IMediator mediator)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        return new AuthController(mediator, environment.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
