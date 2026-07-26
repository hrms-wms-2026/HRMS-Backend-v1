using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.Commands.MfaVerify;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class AuthMfaControllerTests
{
    [Fact]
    public async Task VerifyMfa_SuccessThenLegalAcceptanceRequired_DeletesChallengeCookieAndSetsLegalCookies()
    {
        // Verification success routes into RequiresLegalAcceptance rather than a full sign-in, so
        // this exercises the real "MFA then legal" continuation without needing a DI-backed
        // IAuthenticationService (which SignInAsync would require).
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<VerifyMfaCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto(
                CsrfToken: "csrf",
                CsrfTokenHash: "hash",
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
                User: new CurrentUserDto(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.test"),
                RequiresLegalAcceptance: true,
                LegalChallenge: "legal-challenge",
                LegalCsrfToken: "legal-csrf")));

        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var controller = new AuthMfaController(mediator.Object, environment.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers.Cookie = "onevo_mfa=opaque-challenge";

        await controller.VerifyMfa(new VerifyMfaRequest("123456"), CancellationToken.None);

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("onevo_mfa=");
        setCookie.Should().Contain("path=/api/v1/auth/mfa/verify");
        setCookie.Should().Contain("onevo_legal_pending=");
        setCookie.Should().Contain("path=/api/v1/legal/acceptances/complete-login");
        setCookie.Should().Contain("onevo_legal_csrf=");
    }

    [Fact]
    public async Task VerifyMfa_MissingChallengeCookie_Returns401()
    {
        var mediator = new Mock<IMediator>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var controller = new AuthMfaController(mediator.Object, environment.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.VerifyMfa(new VerifyMfaRequest("123456"), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
        mediator.Verify(
            instance => instance.Send(It.IsAny<VerifyMfaCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
