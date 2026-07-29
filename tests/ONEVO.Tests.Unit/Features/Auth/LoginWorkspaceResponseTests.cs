using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;
using ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Controller-level coverage for the workspace object added to every tenant-resolved auth response
/// (see docs/LOGIN_WORKSPACE_RESPONSE_FIX_REPORT.md). These exercise AuthLoginController directly
/// against a bare DefaultHttpContext (RequestServices left null); TenantAuthResponseWriter's cookie
/// writers never resolve RequestServices, so no DI container is needed.
/// </summary>
public sealed class LoginWorkspaceResponseTests
{
    private static readonly WorkspaceResponseDto Workspace = new("acme", "Acme Inc");
    private static readonly CurrentUserDto User = new(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.test");

    private static readonly TenantSessionExchangeMaterial ExchangeMaterial = new(
        "http://acme.localhost:4200/auth/continue?code=raw-exchange-code",
        DateTimeOffset.Parse("2026-07-29T12:02:00Z"));

    [Fact]
    public async Task Login_SingleTenantSuccess_ReturnsTenantSessionExchange_NotDirectAuthentication()
    {
        // Base-domain login must never sign in directly, even when every gate clears - it always
        // hands off to the tenant host via a one-time exchange code (TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md).
        var session = new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: User,
            Workspace: Workspace,
            TenantSessionExchange: ExchangeMaterial);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<BaseLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(session, null)));

        var (controller, authenticationService) = CreateControllerWithAuthMock(mediator.Object);

        var result = await controller.Login(new LoginRequest("owner@acme.test", "Password123!"), CancellationToken.None);

        var accepted = result.Should().BeOfType<ObjectResult>().Subject;
        accepted.StatusCode.Should().Be(202);
        var body = accepted.Value.Should().BeOfType<TenantSessionExchangeResponseDto>().Subject;
        body.Authenticated.Should().BeFalse();
        body.RedirectRequired.Should().BeTrue();
        body.ContinueUrl.Should().Be(ExchangeMaterial.ContinueUrl);
        body.Workspace.Slug.Should().Be("acme");
        body.Workspace.DisplayName.Should().Be("Acme Inc");
        authenticationService.Verify(
            a => a.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Never,
            "the base host must never sign in - only the tenant host's session-exchange endpoint does");
    }

    [Fact]
    public async Task Login_MfaRequired_IncludesWorkspace()
    {
        var session = new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: User,
            Workspace: Workspace,
            RequiresMfa: true,
            MfaChallenge: "raw-mfa-challenge");

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<BaseLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(session, null)));

        var controller = CreateController(mediator.Object);

        var result = await controller.Login(new LoginRequest("owner@acme.test", "Password123!"), CancellationToken.None);

        var accepted = result.Should().BeOfType<ObjectResult>().Subject;
        accepted.StatusCode.Should().Be(202);
        var body = accepted.Value.Should().BeOfType<AuthSessionResponseDto>().Subject;
        body.MfaRequired.Should().BeTrue();
        body.Workspace.Should().NotBeNull();
        body.Workspace!.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task Login_LegalAcceptanceRequired_IncludesWorkspace()
    {
        var pendingDocs = new[]
        {
            new PendingLegalDocumentDto("privacy_notice", "1.0", "Privacy Notice", null, null, "/api/v1/legal/documents/privacy_notice/1.0", "hash")
        };
        var session = new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: User,
            Workspace: Workspace,
            RequiresLegalAcceptance: true,
            LegalChallenge: "raw-legal-challenge",
            LegalCsrfToken: "raw-legal-csrf",
            PendingLegalDocuments: pendingDocs);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<BaseLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(session, null)));

        var controller = CreateController(mediator.Object);

        var result = await controller.Login(new LoginRequest("owner@acme.test", "Password123!"), CancellationToken.None);

        var accepted = result.Should().BeOfType<ObjectResult>().Subject;
        accepted.StatusCode.Should().Be(202);
        var body = accepted.Value.Should().BeOfType<AuthSessionResponseDto>().Subject;
        body.LegalAcceptanceRequired.Should().BeTrue();
        body.Workspace.Should().NotBeNull();
        body.Workspace!.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task SelectWorkspace_FinalResponse_ReturnsTenantSessionExchange_WithWorkspace()
    {
        var session = new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: User,
            Workspace: Workspace,
            TenantSessionExchange: ExchangeMaterial);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<SelectWorkspaceCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(session));

        var controller = CreateController(mediator.Object);

        var result = await controller.SelectWorkspace(
            new SelectWorkspaceRequest("login-challenge", "acme"), CancellationToken.None);

        var accepted = result.Should().BeOfType<ObjectResult>().Subject;
        accepted.StatusCode.Should().Be(202);
        var body = accepted.Value.Should().BeOfType<TenantSessionExchangeResponseDto>().Subject;
        body.Authenticated.Should().BeFalse();
        body.RedirectRequired.Should().BeTrue();
        body.Workspace.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task Login_TenantSessionExchangeJson_NeverExposesTenantIdOrUserIdOrSessionMaterial()
    {
        var session = new LoginResponseDto(
            CsrfToken: string.Empty,
            CsrfTokenHash: string.Empty,
            ExpiresAt: null,
            User: User,
            Workspace: Workspace,
            TenantSessionExchange: ExchangeMaterial);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<BaseLoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseLoginResultDto>.Success(new BaseLoginResultDto(session, null)));

        var controller = CreateController(mediator.Object);

        var result = await controller.Login(new LoginRequest("owner@acme.test", "Password123!"), CancellationToken.None);
        var accepted = result.Should().BeOfType<ObjectResult>().Subject;
        var body = accepted.Value.Should().BeOfType<TenantSessionExchangeResponseDto>().Subject;

        var json = System.Text.Json.JsonSerializer.Serialize(body);
        json.Should().NotContain("tenant_id");
        json.Should().NotContain("user_id");
        json.Should().NotContain(User.TenantId.ToString());
        json.Should().NotContain(User.UserId.ToString());
        json.Should().NotContain("access_token");
        json.Should().NotContain("refresh_token");
        json.Should().NotContain("permissions");
        json.Should().Contain("\"workspace\"");
        json.Should().Contain("\"slug\":\"acme\"");
        json.Should().Contain("\"display_name\":\"Acme Inc\"");
    }

    private static AuthLoginController CreateController(IMediator mediator) =>
        CreateControllerWithAuthMock(mediator).Controller;

    private static (AuthLoginController Controller, Mock<IAuthenticationService> AuthenticationService)
        CreateControllerWithAuthMock(IMediator mediator)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(instance => instance.ContextMode).Returns(TenantContextMode.System);

        // No test in this file reaches a direct sign-in anymore (base-domain login always hands off
        // via TenantSessionExchange), but IAuthenticationService is still resolved defensively by
        // TenantAuthResponseWriter's plumbing, so it stays registered.
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(instance => instance.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(authenticationService.Object);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var controller = new AuthLoginController(mediator, environment.Object, tenantContext.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }
            }
        };

        return (controller, authenticationService);
    }
}
