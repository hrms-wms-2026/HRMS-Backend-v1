using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.Screenshot;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class ScreenshotCommandSchedulerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<IVerificationRepository> _verification = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    [Fact]
    public async Task TrySchedule_ZeroInputPastIdleThreshold_CreatesConsentCommand()
    {
        var agent = ConfigureEligibleRuntime();
        var snapshot = new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = agent.EmployeeId!.Value,
            KeyboardEventsCount = 0,
            MouseEventsCount = 0,
            IdleSeconds = 901,
            CapturedAt = Now
        };

        var scheduled = await CreateScheduler().TryScheduleAsync(
            agent,
            snapshot,
            CancellationToken.None);

        Assert.True(scheduled);
        _agents.Verify(repository => repository.AddCommandAsync(
            It.Is<AgentCommand>(command =>
                command.CommandType == "screenshot_capture_request" &&
                command.Status == "pending" &&
                command.AgentId == agent.Id &&
                command.EmployeeId == agent.EmployeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task TrySchedule_AnyInput_DoesNotCreateScreenshotCommand(
        int keyboardEvents,
        int mouseEvents)
    {
        var agent = ConfigureEligibleRuntime();
        var snapshot = new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = agent.EmployeeId!.Value,
            KeyboardEventsCount = keyboardEvents,
            MouseEventsCount = mouseEvents,
            IdleSeconds = 1000,
            CapturedAt = Now
        };

        var scheduled = await CreateScheduler().TryScheduleAsync(
            agent,
            snapshot,
            CancellationToken.None);

        Assert.False(scheduled);
        _agents.Verify(repository => repository.AddCommandAsync(
            It.IsAny<AgentCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private ScreenshotCommandScheduler CreateScheduler() => new(
        _agents.Object,
        _attendance.Object,
        _profiles.Object,
        _verification.Object,
        new EffectiveAgentPolicyResolver(),
        _clock.Object);

    private RegisteredAgent ConfigureEligibleRuntime()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        };
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = employee.TenantId,
            EmployeeId = employee.Id,
            DeviceId = $"device-{Guid.NewGuid():N}",
            Status = "active"
        };
        _clock.SetupGet(provider => provider.UtcNow).Returns(Now);
        _agents.Setup(repository => repository.GetActiveSessionByDeviceIdAsync(
                agent.DeviceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSession
            {
                TenantId = agent.TenantId,
                EmployeeId = employee.Id,
                DeviceId = agent.DeviceId,
                IsActive = true
            });
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                TenantId = agent.TenantId,
                EmployeeId = employee.Id,
                DeviceId = agent.Id,
                SessionStart = Now.AddHours(-1)
            });
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _verification.Setup(repository => repository.GetLatestConsentAsync(
                agent.TenantId,
                employee.UserId,
                "monitoring",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GdprConsentRecord
            {
                TenantId = agent.TenantId,
                UserId = employee.UserId,
                ConsentType = "monitoring",
                Consented = true,
                NoticeVersion = "1.0"
            });
        _agents.Setup(repository => repository.GetPolicyByAgentIdAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPolicy
            {
                TenantId = agent.TenantId,
                AgentId = agent.Id,
                PolicyJson =
                    """
                    {
                      "activity_monitoring": true,
                      "screenshot_capture": true,
                      "idle_threshold_seconds": 900,
                      "screenshot_consent_timeout_seconds": 30,
                      "screenshot_cooldown_seconds": 900,
                      "screenshot_scope": "active_monitor",
                      "max_screenshot_bytes": 2097152
                    }
                    """
            });
        _agents.Setup(repository => repository.GetLatestCommandAsync(
                agent.Id,
                "screenshot_capture_request",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentCommand?)null);

        return agent;
    }
}
