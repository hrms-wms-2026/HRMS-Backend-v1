using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.Commands.AdminGoogleLogin;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AdminAuthControllerGoogleCallbackTests
{
    [Fact]
    public async Task GoogleCallback_WhenMfaRequired_SetsChallengeCookieAndReturns202WithoutSession()
    {
        // No IAuthenticationService is registered on this HttpContext, so a SignInAsync call
        // would throw: the test proves the MFA branch never issues an admin session.
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<AdminGoogleLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AdminLoginResultDto>.Success(new AdminLoginResultDto(
                CsrfToken: string.Empty,
                CsrfTokenHash: string.Empty,
                ExpiresAt: null,
                PlatformUserId: Guid.Empty,
                Email: string.Empty,
                PlatformRole: string.Empty,
                RequiresMfa: true,
                MfaSessionToken: "mfa-challenge",
                Permissions: Array.Empty<string>())));

        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var controller = new AdminAuthController(
            mediator.Object,
            environment.Object,
            new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.GoogleCallback(
            new AdminGoogleCallbackRequest("google-id-token"), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(202);
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("admin_mfa=mfa-challenge");
        setCookie.Should().Contain("path=/admin/v1/auth");
        setCookie.Should().NotContain("admin_csrf=");
    }
}
