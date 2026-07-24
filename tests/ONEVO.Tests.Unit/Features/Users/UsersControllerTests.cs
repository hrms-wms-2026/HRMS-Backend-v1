using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Users;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Users.DTOs;
using ONEVO.Application.Features.Users.Queries.GetMyProfile;

namespace ONEVO.Tests.Unit.Features.Users;

public sealed class UsersControllerTests
{
    [Fact]
    public void GetMe_RequiresTenantAuthenticationAndUsesCanonicalRoute()
    {
        var controllerType = typeof(UsersController);
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var endpoint = controllerType.GetMethod(nameof(UsersController.GetMe))!
            .GetCustomAttribute<HttpGetAttribute>();

        Assert.Equal("TenantPolicy", authorize?.Policy);
        Assert.Equal("api/v1/users", route?.Template);
        Assert.Equal("me", endpoint?.Template);
    }

    [Fact]
    public async Task GetMe_WhenAuthenticationIsRejected_Returns401Problem()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<GetMyProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MyProfileDto>.Failure("Authentication required.", StatusCodes.Status401Unauthorized));
        var controller = CreateController(mediator.Object);

        var result = await controller.GetMe(CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task GetMe_ReturnsStableSnakeCaseProfileShape()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Send(It.IsAny<GetMyProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MyProfileDto>.Success(CreateProfile()));
        var controller = CreateController(mediator.Object);

        var result = await controller.GetMe(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        AssertPropertyNames(
            json.RootElement,
            "devices", "email", "employee", "face_scan", "tenant_id", "user_id", "work_location");
        AssertPropertyNames(
            json.RootElement.GetProperty("employee"),
            "email", "employee_number", "employment_status_id", "first_name", "hire_date",
            "id", "last_name", "phone", "work_mode_id");
        AssertPropertyNames(
            json.RootElement.GetProperty("devices")[0],
            "agent_id", "agent_version", "device_name", "last_heartbeat_at", "os_version", "status");
        AssertPropertyNames(
            json.RootElement.GetProperty("work_location"),
            "grace_period_minutes", "photo_challenge_on_mismatch", "verification_enabled", "work_mode");
        AssertPropertyNames(
            json.RootElement.GetProperty("face_scan"),
            "captured_at", "is_active", "source", "status");
    }

    private static UsersController CreateController(IMediator mediator) =>
        new(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static MyProfileDto CreateProfile() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ada@example.test",
            new EmployeeProfileDto(
                Guid.NewGuid(),
                "EMP-001",
                "Ada",
                "Lovelace",
                "ada@example.test",
                null,
                new DateOnly(2026, 1, 1),
                1,
                1),
            [
                new MyDeviceDto(
                    Guid.NewGuid(),
                    "Ada laptop",
                    "Windows 11",
                    "1.0.0",
                    "active",
                    DateTimeOffset.Parse("2026-07-24T10:00:00+00:00"))
            ],
            new WorkLocationDto("hybrid", true, 10, true),
            new FaceScanDto(
                "approved",
                "hr_verified_profile",
                DateTimeOffset.Parse("2026-07-20T10:00:00+00:00"),
                true));

    private static void AssertPropertyNames(JsonElement element, params string[] expected)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }
}
