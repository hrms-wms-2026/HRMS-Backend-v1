using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.IngestBatch;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class IngestBatchCommandHandlerTests
{
    private static readonly Guid FixedEventId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    private static readonly Guid FixedPresenceSessionId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901");

    private const string ValidPayload =
        """{"timestamp":"2026-07-26T10:00:00Z","batch":[{"event_id":"a1b2c3d4-e5f6-7890-abcd-ef1234567890","type":"activity_snapshot","schema_version":"1","captured_at":"2026-07-26T10:00:00+00:00","presence_session_id":"b2c3d4e5-f6a7-8901-bcde-f12345678901","task_id":null,"data":{"keyboard_events_count":4,"mouse_events_count":2,"active_seconds":30,"idle_seconds":0,"foreground_process_name":"code.exe"}}]}""";

    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    [Fact]
    public async Task Handle_TenantClaimDoesNotMatchAgent_DeniesWithoutSaving()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, Guid.NewGuid(), ValidPayload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Handle_MonitoringPolicyDisabled_DeniesWithoutSaving()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(
            agent,
            policyJson: """{"activity_monitoring":false}""");

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, ValidPayload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Handle_NoOpenClockInDeviceSession_DeniesWithoutSaving()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent, includeDeviceSession: false);

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, ValidPayload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Handle_ValidSelfBoundActiveWorkSession_StoresClaimScopedBatch()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, ValidPayload),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _agents.Verify(repository => repository.AddRawActivityBatchAsync(
            It.Is<ActivityRawBuffer>(batch =>
                batch.AgentDeviceId == agent.Id &&
                batch.TenantId == agent.TenantId &&
                batch.PayloadJson == ValidPayload),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private IngestBatchCommandHandler CreateHandler() =>
        new(_agents.Object, _attendance.Object, _uow.Object);

    private void ConfigureActiveMonitoring(
        RegisteredAgent agent,
        string policyJson = """{"activity_monitoring":true}""",
        bool includeDeviceSession = true)
    {
        _agents.Setup(repository => repository.GetAgentByIdAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _agents.Setup(repository => repository.GetActiveSessionByDeviceIdAsync(
                agent.DeviceId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentSession
            {
                TenantId = agent.TenantId,
                DeviceId = agent.DeviceId,
                EmployeeId = agent.EmployeeId!.Value,
                IsActive = true
            });
        _agents.Setup(repository => repository.GetPolicyByAgentIdAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPolicy
            {
                TenantId = agent.TenantId,
                AgentId = agent.Id,
                PolicyJson = policyJson
            });
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(includeDeviceSession
                ? new DeviceSession
                {
                    TenantId = agent.TenantId,
                    EmployeeId = agent.EmployeeId.Value,
                    DeviceId = agent.Id,
                    SessionStart = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
                : null);
    }

    private void VerifyNothingSaved()
    {
        _agents.Verify(repository => repository.AddRawActivityBatchAsync(
            It.IsAny<ActivityRawBuffer>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Envelope validation (RED → should fail until handler validates these) ──

    [Fact]
    public async Task Handle_BatchEventMissingEventId_Returns400()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);

        var payload =
            """{"timestamp":"2026-07-26T10:00:00Z","batch":[{"type":"activity_snapshot","schema_version":"1","captured_at":"2026-07-26T10:00:00+00:00","presence_session_id":"b2c3d4e5-f6a7-8901-bcde-f12345678901","data":{}}]}""";

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, payload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_BatchEventMissingCapturedAt_Returns400()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);

        var payload =
            """{"timestamp":"2026-07-26T10:00:00Z","batch":[{"event_id":"a1b2c3d4-e5f6-7890-abcd-ef1234567890","type":"activity_snapshot","schema_version":"1","presence_session_id":"b2c3d4e5-f6a7-8901-bcde-f12345678901","data":{}}]}""";

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, payload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_BatchEventMissingPresenceSessionId_Returns400()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);

        var payload =
            """{"timestamp":"2026-07-26T10:00:00Z","batch":[{"event_id":"a1b2c3d4-e5f6-7890-abcd-ef1234567890","type":"activity_snapshot","schema_version":"1","captured_at":"2026-07-26T10:00:00+00:00","data":{}}]}""";

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, payload),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MeetingAppUsageType_Succeeds()
    {
        var agent = CreateActiveAgent();
        ConfigureActiveMonitoring(agent);
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var payload =
            """{"timestamp":"2026-07-26T10:00:00Z","batch":[{"event_id":"a1b2c3d4-e5f6-7890-abcd-ef1234567890","type":"meeting_app_usage","schema_version":"1","captured_at":"2026-07-26T10:00:00+00:00","presence_session_id":"b2c3d4e5-f6a7-8901-bcde-f12345678901","task_id":null,"data":{"process_name":"teams.exe","duration_seconds":3600,"camera_active":false,"microphone_active":true}}]}""";

        var result = await CreateHandler().Handle(
            new IngestBatchCommand(agent.Id, agent.TenantId, payload),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static RegisteredAgent CreateActiveAgent() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        DeviceId = $"device-{Guid.NewGuid():N}",
        Status = "active"
    };
}
