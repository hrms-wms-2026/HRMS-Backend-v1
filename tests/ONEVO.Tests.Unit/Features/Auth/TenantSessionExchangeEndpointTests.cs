using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Controller-level coverage for POST /api/v1/auth/session-exchange (AuthSessionController), the
/// tenant-host-only endpoint that consumes a one-time exchange code and creates the real, host-
/// scoped tenant session. See TENANT_SESSION_EXCHANGE_LOGIN_FLOW_REPORT.md.
/// </summary>
public sealed class TenantSessionExchangeEndpointTests
{
    private static readonly CurrentUserDto User = new(Guid.NewGuid(), Guid.NewGuid(), "owner@acme.test");
    private static readonly WorkspaceResponseDto Workspace = new("acme", "Acme Inc");

    [Fact]
    public async Task SessionExchange_CalledOnBaseHost_Returns400_AndNeverConsumesCode()
    {
        var tenantSessionExchange = new Mock<ITenantSessionExchangeService>();
        var (controller, _) = CreateController(tenantSessionExchange.Object, TenantContextMode.System);

        var result = await controller.SessionExchange(new TenantSessionExchangeRequest("code"), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        tenantSessionExchange.Verify(
            s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SessionExchange_InvalidCode_ReturnsGenericFailure_WithoutSigningIn()
    {
        var tenantId = Guid.NewGuid();
        var tenantSessionExchange = new Mock<ITenantSessionExchangeService>();
        tenantSessionExchange
            .Setup(s => s.ConsumeAsync("bad-code", tenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Failure(
                "This sign-in link is invalid or has expired. Please sign in again.", 401));

        var (controller, authenticationService) = CreateController(tenantSessionExchange.Object, TenantContextMode.Tenant, tenantId);

        var result = await controller.SessionExchange(new TenantSessionExchangeRequest("bad-code"), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(401);
        authenticationService.Verify(
            a => a.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [Fact]
    public async Task SessionExchange_ValidCode_SignsIn_AndReturnsAuthenticatedSessionWithWorkspace()
    {
        var tenantId = Guid.NewGuid();
        var sessionDto = new LoginResponseDto(
            CsrfToken: "raw-csrf",
            CsrfTokenHash: "hash-csrf",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(8),
            User: User,
            Permissions: ["people.read"],
            ActiveModules: ["people"],
            Workspace: Workspace);

        var tenantSessionExchange = new Mock<ITenantSessionExchangeService>();
        tenantSessionExchange
            .Setup(s => s.ConsumeAsync("good-code", tenantId, "1.2.3.4", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var (controller, authenticationService) = CreateController(tenantSessionExchange.Object, TenantContextMode.Tenant, tenantId, remoteIp: "1.2.3.4");

        var result = await controller.SessionExchange(new TenantSessionExchangeRequest("good-code"), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<AuthSessionResponseDto>().Subject;
        body.Authenticated.Should().BeTrue();
        body.Workspace.Should().NotBeNull();
        body.Workspace!.Slug.Should().Be("acme");
        authenticationService.Verify(
            a => a.SignInAsync(
                It.IsAny<HttpContext>(), "TenantScheme", It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()),
            Times.Once);

        var setCookieHeader = controller.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("onevo_csrf=");
    }

    [Fact]
    public async Task SessionExchange_DoesNotAcceptTenantSlugOrIdFromRequestBody()
    {
        // TenantSessionExchangeRequest only ever carries "code" - the tenant is resolved solely
        // from ITenantContext (the request host), never from anything client-supplied.
        var properties = typeof(TenantSessionExchangeRequest).GetProperties();
        properties.Should().ContainSingle();
        properties[0].Name.Should().Be("Code");
    }

    private static (AuthSessionController Controller, Mock<IAuthenticationService> AuthenticationService) CreateController(
        ITenantSessionExchangeService tenantSessionExchange,
        TenantContextMode contextMode,
        Guid? tenantId = null,
        string? remoteIp = null)
    {
        var mediator = new Mock<IMediator>();
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(instance => instance.ContextMode).Returns(contextMode);
        tenantContext.Setup(instance => instance.TenantId).Returns(tenantId ?? Guid.Empty);

        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(instance => instance.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(authenticationService.Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Problem(...) resolves ProblemDetailsFactory from RequestServices; AddMvcCore is the
        // minimal registration that provides it without pulling in unrelated MVC pipeline pieces.
        services.AddLogging();
        services.AddMvcCore();

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (remoteIp is not null)
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);

        var controller = new AuthSessionController(mediator.Object, environment.Object, tenantContext.Object, tenantSessionExchange)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, authenticationService);
    }
}
