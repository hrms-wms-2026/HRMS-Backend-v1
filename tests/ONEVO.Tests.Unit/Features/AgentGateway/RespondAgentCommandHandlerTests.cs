using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.RespondAgentCommand;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class RespondAgentCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_Deny_WritesConsentEventToMonitoringConsentEvents()
    {
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            DeviceId = "approved-device",
            Status = "active"
        };
        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            AgentId = agent.Id,
            EmployeeId = agent.EmployeeId.Value,
            CommandType = "screenshot_capture_request",
            Status = "pending",
            ExpiresAt = Now.AddSeconds(20)
        };
        var agents = new Mock<IAgentGatewayRepository>();
        var attendance = new Mock<ITimeAttendanceRepository>();
        var monitoring = new Mock<IActivityMonitoringRepository>();
        var clock = new Mock<IDateTimeProvider>();
        var uow = new Mock<IUnitOfWork>();

        agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        agents.Setup(r => r.GetCommandByIdAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(command);
        attendance.Setup(r => r.GetOpenDeviceSessionAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                TenantId = agent.TenantId,
                EmployeeId = agent.EmployeeId.Value,
                DeviceId = agent.Id,
                SessionStart = Now.AddHours(-1)
            });
        monitoring.Setup(m => m.AddConsentEventAsync(
                It.IsAny<MonitoringConsentEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        clock.SetupGet(p => p.UtcNow).Returns(Now);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await new RespondAgentCommandHandler(
            agents.Object, attendance.Object, monitoring.Object, clock.Object, uow.Object)
            .Handle(
                new RespondAgentCommand(agent.Id, command.Id, "deny", "monitoring-screenshot-v1"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("denied", command.Status);
        Assert.Equal("deny", command.Decision);
        Assert.Equal(Now, command.DecisionAt);
        monitoring.Verify(m => m.AddConsentEventAsync(
            It.Is<MonitoringConsentEvent>(e =>
                e.TenantId == agent.TenantId &&
                e.EmployeeId == agent.EmployeeId.Value &&
                e.AgentDeviceId == agent.Id &&
                e.IncidentId == command.Id &&
                e.Decision == "denied" &&
                e.OccurredAt == Now),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
