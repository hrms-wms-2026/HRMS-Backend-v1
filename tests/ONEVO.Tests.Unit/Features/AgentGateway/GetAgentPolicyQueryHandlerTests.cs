using Moq;
using ONEVO.Application.Features.AgentGateway.Policy;
using ONEVO.Application.Features.AgentGateway.Queries.GetAgentPolicy;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class GetAgentPolicyQueryHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<IVerificationRepository> _verification = new();

    [Fact]
    public async Task Handle_ApprovedDevicePresenceAndConsent_ReturnsEnabledEffectivePolicy()
    {
        var agent = ConfigureRuntime(monitoringConsent: true);

        var result = await CreateHandler().Handle(
            new GetAgentPolicyQuery(agent.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ActivityMonitoring);
        Assert.True(result.Value.ApplicationTracking);
        Assert.True(result.Value.ScreenshotCapture);
    }

    [Fact]
    public async Task Handle_LatestMonitoringConsentDenied_ReturnsDisabledEffectivePolicy()
    {
        var agent = ConfigureRuntime(monitoringConsent: false);

        var result = await CreateHandler().Handle(
            new GetAgentPolicyQuery(agent.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.ActivityMonitoring);
        Assert.False(result.Value.ApplicationTracking);
        Assert.False(result.Value.ScreenshotCapture);
    }

    [Fact]
    public async Task Handle_NoOpenDeviceSession_ReturnsDisabledEffectivePolicy()
    {
        var agent = ConfigureRuntime(monitoringConsent: true, activePresence: false);

        var result = await CreateHandler().Handle(
            new GetAgentPolicyQuery(agent.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.ActivityMonitoring);
    }

    private GetAgentPolicyQueryHandler CreateHandler() => new(
        _agents.Object,
        _attendance.Object,
        _profiles.Object,
        _verification.Object,
        new EffectiveAgentPolicyResolver());

    private RegisteredAgent ConfigureRuntime(
        bool monitoringConsent,
        bool activePresence = true)
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

        _agents.Setup(repository => repository.GetAgentByIdAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
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
                      "application_tracking": true,
                      "screenshot_capture": true
                    }
                    """
            });
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
            .ReturnsAsync(activePresence
                ? new DeviceSession
                {
                    TenantId = agent.TenantId,
                    EmployeeId = employee.Id,
                    DeviceId = agent.Id,
                    SessionStart = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
                : null);
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
                Consented = monitoringConsent,
                NoticeVersion = "1.0"
            });

        return agent;
    }
}
