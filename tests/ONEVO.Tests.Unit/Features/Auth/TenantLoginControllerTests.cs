using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MediatR;
using Moq;
using ONEVO.Api.Contracts.Auth;
using ONEVO.Api.Controllers.Tenant.Auth;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Queries.GetAdminGoogleSsoConfig;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

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
        setCookie.Should().NotContain("crownus_session=");
        setCookie.Should().NotContain("crownus_mfa=");
    }

    [Fact]
    public async Task GoogleConfig_OnRootHost_WhenEnabled_ReturnsEnabledAndClientId()
    {
        var mediator = MediatorReturning(new AdminGoogleSsoConfigDto
        {
            Enabled = true,
            ClientId = "client-123.apps.googleusercontent.com"
        });
        var controller = CreateController(mediator.Object, TenantContextMode.System);

        var result = await controller.GoogleConfig(CancellationToken.None);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AdminGoogleSsoConfigDto>().Subject;
        dto.Enabled.Should().BeTrue();
        dto.ClientId.Should().Be("client-123.apps.googleusercontent.com");
        mediator.Verify(
            m => m.Send(It.IsAny<GetAdminGoogleSsoConfigQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleConfig_OnRootHost_WhenDisabled_ReturnsEnabledFalseAndNoClientId()
    {
        var mediator = MediatorReturning(new AdminGoogleSsoConfigDto { Enabled = false, ClientId = null });
        var controller = CreateController(mediator.Object, TenantContextMode.System);

        var result = await controller.GoogleConfig(CancellationToken.None);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AdminGoogleSsoConfigDto>().Subject;
        dto.Enabled.Should().BeFalse();
        dto.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task GoogleConfig_WhenDisabled_NeverLeaksAClientId()
    {
        // Defence in depth: even if the query ever returned a clientId alongside enabled=false,
        // the anonymous endpoint must not reveal it.
        var mediator = MediatorReturning(new AdminGoogleSsoConfigDto { Enabled = false, ClientId = "stale-id" });
        var controller = CreateController(mediator.Object, TenantContextMode.System);

        var result = await controller.GoogleConfig(CancellationToken.None);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AdminGoogleSsoConfigDto>().Subject;
        dto.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task GoogleConfig_SerializesOnlyEnabledAndClientId()
    {
        var mediator = MediatorReturning(new AdminGoogleSsoConfigDto { Enabled = true, ClientId = "client-123" });
        var controller = CreateController(mediator.Object, TenantContextMode.System);

        var result = await controller.GoogleConfig(CancellationToken.None);

        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "enabled", "clientId" });
    }

    [Fact]
    public async Task GoogleConfig_OnTenantHost_IsRejected_AndNeverCallsMediator()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object, TenantContextMode.Tenant);

        var result = await controller.GoogleConfig(CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        mediator.Verify(
            m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GoogleConfig_IsAnAnonymousGetOnGoogleConfigRoute()
    {
        var method = typeof(AuthLoginController).GetMethod(nameof(AuthLoginController.GoogleConfig))!;

        method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Should().NotBeEmpty();
        method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>()
            .Single()
            .Template.Should().Be("google-config");
    }

    private static Mock<IMediator> MediatorReturning(AdminGoogleSsoConfigDto dto)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetAdminGoogleSsoConfigQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AdminGoogleSsoConfigDto>.Success(dto));
        return mediator;
    }

    private static AuthLoginController CreateController(IMediator mediator)
        => CreateController(mediator, TenantContextMode.Tenant);

    private static AuthLoginController CreateController(IMediator mediator, TenantContextMode mode)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(instance => instance.EnvironmentName).Returns(Environments.Development);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(instance => instance.ContextMode).Returns(mode);

        return new AuthLoginController(mediator, environment.Object, tenantContext.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
