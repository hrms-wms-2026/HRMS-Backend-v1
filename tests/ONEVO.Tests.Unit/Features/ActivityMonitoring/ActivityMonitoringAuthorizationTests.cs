using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.ActivityMonitoring;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.Access;
using ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetAppUsage;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetEmployeeDailySummary;
using ONEVO.Application.Features.ActivityMonitoring.Queries.GetMeetings;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

public sealed class ActivityMonitoringAuthorizationTests
{
    private static readonly Type ControllerType = typeof(ActivityMonitoringController);

    // ── Manager route permission declarations ──────────────────────────────────

    [Theory]
    [InlineData(nameof(ActivityMonitoringController.GetDailySummary))]
    [InlineData(nameof(ActivityMonitoringController.GetSnapshots))]
    [InlineData(nameof(ActivityMonitoringController.GetAppUsage))]
    [InlineData(nameof(ActivityMonitoringController.GetMeetings))]
    public void ManagerRoute_DeclaresMonitoringReadPermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("monitoring:read", attribute.Permission);
    }

    // ── Self-route identity resolution ─────────────────────────────────────────

    [Fact]
    public async Task GetMySummary_ResolvesEmployeeIdFromUserId_NotDirectlyFromUserId()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Assert.NotEqual(userId, employeeId);

        var (controller, mediator, _) = CreateControllerWithIdentity(userId, employeeId);

        await controller.GetMySummary(new DateOnly(2026, 1, 1), CancellationToken.None);

        // Self-view now dispatches GetEmployeeDailySummaryQuery (privacy-separated with consent notices)
        mediator.Verify(m => m.Send(
            It.Is<GetEmployeeDailySummaryQuery>(q => q.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);

        mediator.Verify(m => m.Send(
            It.Is<GetEmployeeDailySummaryQuery>(q => q.EmployeeId == userId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyAppUsage_ResolvesEmployeeIdFromUserId_NotDirectlyFromUserId()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Assert.NotEqual(userId, employeeId);

        var (controller, mediator, _) = CreateControllerWithIdentity(userId, employeeId);

        await controller.GetMyAppUsage(new DateOnly(2026, 1, 1), CancellationToken.None);

        mediator.Verify(m => m.Send(
            It.Is<GetAppUsageQuery>(q => q.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);

        mediator.Verify(m => m.Send(
            It.Is<GetAppUsageQuery>(q => q.EmployeeId == userId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyMeetings_ResolvesEmployeeIdFromUserId_NotDirectlyFromUserId()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Assert.NotEqual(userId, employeeId);

        var (controller, mediator, _) = CreateControllerWithIdentity(userId, employeeId);

        await controller.GetMyMeetings(new DateOnly(2026, 1, 1), CancellationToken.None);

        mediator.Verify(m => m.Send(
            It.Is<GetMeetingsQuery>(q => q.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);

        mediator.Verify(m => m.Send(
            It.Is<GetMeetingsQuery>(q => q.EmployeeId == userId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMySummary_WhenUserNotAuthenticated_Returns401()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(false);
        currentUser.SetupGet(u => u.UserId).Returns(Guid.Empty);

        var controller = CreateController(
            new Mock<IMediator>().Object,
            currentUser.Object,
            new Mock<IUserProfileRepository>().Object,
            new Mock<IMonitoringAccessResolver>().Object);

        var result = await controller.GetMySummary(new DateOnly(2026, 1, 1), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (ActivityMonitoringController controller, Mock<IMediator> mediator, Mock<IUserProfileRepository> profiles)
        CreateControllerWithIdentity(Guid userId, Guid employeeId)
    {
        var mediator = new Mock<IMediator>();
        var currentUser = new Mock<ICurrentUser>();
        var profiles = new Mock<IUserProfileRepository>();
        var access = new Mock<IMonitoringAccessResolver>();

        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUser.SetupGet(u => u.UserId).Returns(userId);

        profiles.Setup(p => p.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = employeeId,
                UserId = userId,
                TenantId = Guid.NewGuid(),
                EmployeeNumber = "EMP-001",
                FirstName = "Test",
                Email = "test@example.test",
                HireDate = new DateOnly(2026, 1, 1)
            });

        mediator.Setup(m => m.Send(It.IsAny<GetDailySummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ActivityDailySummaryDto>.Failure("test"));
        mediator.Setup(m => m.Send(It.IsAny<GetEmployeeDailySummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeActivityDailySummaryDto?>.Failure("test"));
        mediator.Setup(m => m.Send(It.IsAny<GetAppUsageQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ApplicationUsageDto>>.Failure("test"));
        mediator.Setup(m => m.Send(It.IsAny<GetMeetingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<MeetingSessionDto>>.Failure("test"));

        var controller = CreateController(mediator.Object, currentUser.Object, profiles.Object, access.Object);
        return (controller, mediator, profiles);
    }

    private static ActivityMonitoringController CreateController(
        IMediator mediator,
        ICurrentUser currentUser,
        IUserProfileRepository profiles,
        IMonitoringAccessResolver access) =>
        new(mediator, currentUser, profiles, access)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}
