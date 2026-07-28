using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.RespondAgentCommand;
using ONEVO.Application.Features.AgentGateway.Queries.GetPendingAgentCommands;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class AgentCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    // ── Tenant / agent isolation ───────────────────────────────────────────────

    [Fact]
    public async Task GetPendingCommands_CrossTenantCommand_IsFilteredOut()
    {
        var agent = ActiveAgent();
        var crossTenantCommand = PendingCommand(agent);
        crossTenantCommand.TenantId = Guid.NewGuid();

        var (agents, attendance, clock) = BuildGetDeps(agent, [crossTenantCommand]);

        var result = await new GetPendingAgentCommandsQueryHandler(
                agents.Object, attendance.Object, clock.Object)
            .Handle(new GetPendingAgentCommandsQuery(agent.Id, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetPendingCommands_CrossAgentEmployee_IsFilteredOut()
    {
        var agent = ActiveAgent();
        var otherEmployeeCommand = PendingCommand(agent);
        otherEmployeeCommand.EmployeeId = Guid.NewGuid();

        var (agents, attendance, clock) = BuildGetDeps(agent, [otherEmployeeCommand]);

        var result = await new GetPendingAgentCommandsQueryHandler(
                agents.Object, attendance.Object, clock.Object)
            .Handle(new GetPendingAgentCommandsQuery(agent.Id, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GetPendingCommands_InactiveAgent_ReturnsForbidden()
    {
        var agent = ActiveAgent();
        agent.Status = "revoked";
        var agents = new Mock<IAgentGatewayRepository>();
        agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var result = await new GetPendingAgentCommandsQueryHandler(
                agents.Object,
                new Mock<ITimeAttendanceRepository>().Object,
                new Mock<IDateTimeProvider>().Object)
            .Handle(new GetPendingAgentCommandsQuery(agent.Id, 20), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RespondAgentCommand_AfterExpiry_ReturnsConflict()
    {
        var agent = ActiveAgent();
        var command = PendingCommand(agent);
        command.ExpiresAt = Now.AddSeconds(-1);

        var result = await HandleRespond(agent, command, "allow");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("expired", command.Status);
    }

    // ── Duplicate acknowledgement ─────────────────────────────────────────────

    [Fact]
    public async Task RespondAgentCommand_AlreadyDecided_ReturnsConflict()
    {
        var agent = ActiveAgent();
        var command = PendingCommand(agent);
        command.Status = "accepted";
        command.ExpiresAt = Now.AddMinutes(5);

        var result = await HandleRespond(agent, command, "allow");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    // ── First-terminal-outcome-wins ───────────────────────────────────────────

    [Fact]
    public async Task RespondAgentCommand_AfterDenied_ReturnsConflict()
    {
        var agent = ActiveAgent();
        var command = PendingCommand(agent);
        command.Status = "denied";
        command.ExpiresAt = Now.AddMinutes(5);

        var result = await HandleRespond(agent, command, "allow");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("denied", command.Status);
    }

    [Fact]
    public async Task RespondAgentCommand_AfterExpiredStatus_ReturnsConflict()
    {
        var agent = ActiveAgent();
        var command = PendingCommand(agent);
        command.Status = "expired";
        command.ExpiresAt = Now.AddMinutes(5);

        var result = await HandleRespond(agent, command, "allow");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("expired", command.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static RegisteredAgent ActiveAgent() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        DeviceId = "approved-device",
        Status = "active"
    };

    private static AgentCommand PendingCommand(RegisteredAgent agent) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = agent.TenantId,
        AgentId = agent.Id,
        EmployeeId = agent.EmployeeId!.Value,
        CommandType = "screenshot_capture_request",
        PayloadJson = "{}",
        Status = "pending",
        ExpiresAt = Now.AddMinutes(2)
    };

    private static (Mock<IAgentGatewayRepository> agents, Mock<ITimeAttendanceRepository> attendance, Mock<IDateTimeProvider> clock)
        BuildGetDeps(RegisteredAgent agent, IReadOnlyList<AgentCommand> commands)
    {
        var agents = new Mock<IAgentGatewayRepository>();
        var attendance = new Mock<ITimeAttendanceRepository>();
        var clock = new Mock<IDateTimeProvider>();

        agents.Setup(r => r.GetAgentByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        agents.Setup(r => r.ExpireCommandsAsync(agent.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        agents.Setup(r => r.GetPendingCommandsAsync(agent.Id, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(commands);

        attendance.Setup(r => r.GetOpenDeviceSessionAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                TenantId = agent.TenantId,
                EmployeeId = agent.EmployeeId!.Value,
                DeviceId = agent.Id,
                SessionStart = Now.AddHours(-1)
            });

        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return (agents, attendance, clock);
    }

    private static async Task<ONEVO.Application.Common.Models.Result<AgentCommandDecisionResponse>> HandleRespond(
        RegisteredAgent agent,
        AgentCommand command,
        string decision)
    {
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
                EmployeeId = agent.EmployeeId!.Value,
                DeviceId = agent.Id,
                SessionStart = Now.AddHours(-1)
            });
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return await new RespondAgentCommandHandler(
                agents.Object, attendance.Object, monitoring.Object, clock.Object, uow.Object)
            .Handle(
                new RespondAgentCommand(agent.Id, command.Id, decision, "monitoring-screenshot-v1"),
                CancellationToken.None);
    }
}
