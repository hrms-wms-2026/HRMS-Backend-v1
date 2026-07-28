using Moq;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.RecordConsentEvent;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class RecordConsentEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid AgentDeviceId = Guid.NewGuid();
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 27, 14, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAgentGatewayRepository> _agentRepo = new();
    private readonly Mock<IActivityMonitoringRepository> _repo = new();

    [Theory]
    [InlineData("allowed")]
    [InlineData("denied")]
    [InlineData("timeout")]
    [InlineData("upload_failed_no_image")]
    public async Task Handle_ValidDecision_PersistsWithEmployeeFromAgentAndReturnsSuccess(string decision)
    {
        SetupActiveAgent();
        _repo.Setup(r => r.AddConsentEventAsync(
            It.IsAny<MonitoringConsentEvent>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(MakeCommand(decision), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.AddConsentEventAsync(
            It.Is<MonitoringConsentEvent>(e =>
                e.TenantId == TenantId &&
                e.EmployeeId == EmployeeId &&
                e.IncidentId == IncidentId &&
                e.Decision == decision &&
                e.OccurredAt == OccurredAt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidDecision_ReturnsBadRequest()
    {
        var result = await CreateHandler().Handle(
            MakeCommand("screen_unlocked"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        _agentRepo.Verify(r => r.GetAgentByIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.AddConsentEventAsync(
            It.IsAny<MonitoringConsentEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AgentNotFound_ReturnsUnauthorized()
    {
        _agentRepo.Setup(r => r.GetAgentByIdAsync(AgentDeviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredAgent?)null);

        var result = await CreateHandler().Handle(MakeCommand("denied"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        _repo.Verify(r => r.AddConsentEventAsync(
            It.IsAny<MonitoringConsentEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TenantMismatch_ReturnsForbidden()
    {
        var wrongTenantAgent = new RegisteredAgent
        {
            Id = AgentDeviceId,
            TenantId = Guid.NewGuid(), // different tenant
            EmployeeId = EmployeeId,
            Status = "active"
        };
        _agentRepo.Setup(r => r.GetAgentByIdAsync(AgentDeviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrongTenantAgent);

        var result = await CreateHandler().Handle(MakeCommand("allowed"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _repo.Verify(r => r.AddConsentEventAsync(
            It.IsAny<MonitoringConsentEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateIncidentId_RepositoryReturnsFalse_StillReturnsSuccess()
    {
        SetupActiveAgent();
        _repo.Setup(r => r.AddConsentEventAsync(
            It.IsAny<MonitoringConsentEvent>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(MakeCommand("denied"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private void SetupActiveAgent()
    {
        var agent = new RegisteredAgent
        {
            Id = AgentDeviceId,
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Status = "active"
        };
        _agentRepo.Setup(r => r.GetAgentByIdAsync(AgentDeviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
    }

    private RecordConsentEventCommandHandler CreateHandler() =>
        new(_agentRepo.Object, _repo.Object);

    private RecordConsentEventCommand MakeCommand(string decision) =>
        new(TenantId, AgentDeviceId, IncidentId, decision, OccurredAt);
}
