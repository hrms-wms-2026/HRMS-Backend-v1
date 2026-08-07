using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class CompleteAgentCommandHandlerTests
{
    private readonly Mock<IAgentCommandRepository> _commandsRepo = new();
    private readonly Mock<IEvidenceAssetRepository> _assetsRepo = new();
    private readonly Mock<ITrayActivationRepository> _trayRepo = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _commandId = Guid.NewGuid();

    public CompleteAgentCommandHandlerTests()
    {
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.UserId).Returns(_userId);

        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration { Id = _deviceId, TenantId = _tenantId, UserId = _userId, IsActive = true, ActivatedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
    }

    private CompleteAgentCommandHandler CreateHandler() => new(
        _commandsRepo.Object,
        _assetsRepo.Object,
        _trayRepo.Object,
        _device.Object,
        _clock,
        _uow,
        NullLogger<CompleteAgentCommandHandler>.Instance);

    private AgentCommand MakePendingCommand(Guid? deviceOverride = null) => new()
    {
        Id = _commandId,
        TenantId = _tenantId,
        AgentDeviceId = deviceOverride ?? _deviceId,
        RequestedById = Guid.NewGuid(),
        CommandType = "capture_screenshot",
        Status = "pending",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    [Fact]
    public async Task Handle_CommandNotFound_Returns404()
    {
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentCommand?)null);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, Guid.NewGuid(), DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_WrongDevice_Returns403()
    {
        var otherDeviceId = Guid.NewGuid();
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePendingCommand(deviceOverride: otherDeviceId));

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, Guid.NewGuid(), DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AlreadyCompleted_ReturnsSuccess_Idempotent()
    {
        var cmd = MakePendingCommand();
        cmd.Status = "completed";
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmd);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, null, DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(0);
        _assetsRepo.Verify(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyFailed_Returns409()
    {
        var cmd = MakePendingCommand();
        cmd.Status = "failed";
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmd);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, null, DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ExpiredCommand_Returns410()
    {
        var cmd = MakePendingCommand();
        cmd.Status = "delivered";
        cmd.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmd);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, null, DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(410);
    }

    [Fact]
    public async Task Handle_Success_CreatesEvidenceAssetAndCompletesCommand()
    {
        var cmd = MakePendingCommand();
        cmd.Status = "delivered";
        var fileRecordId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmd);

        MonitoringEvidenceAsset? savedAsset = null;
        _assetsRepo.Setup(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()))
            .Callback<MonitoringEvidenceAsset>(a => savedAsset = a);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, true, null, fileRecordId, capturedAt), default);

        result.IsSuccess.Should().BeTrue();
        savedAsset.Should().NotBeNull();
        savedAsset!.FileRecordId.Should().Be(fileRecordId);
        savedAsset.EvidenceType.Should().Be("screenshot");
        savedAsset.TriggerType.Should().Be("on_demand");
        cmd.Status.Should().Be("completed");
        _uow.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Failure_SetsFailedStatusAndNoAsset()
    {
        var cmd = MakePendingCommand();
        cmd.Status = "delivered";
        _commandsRepo.Setup(r => r.GetByIdAsync(_tenantId, _commandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cmd);

        var result = await CreateHandler().Handle(
            new CompleteAgentCommandCommand(_commandId, false, "{\"error\":\"capture failed\"}", null, DateTimeOffset.UtcNow), default);

        result.IsSuccess.Should().BeTrue();
        cmd.Status.Should().Be("failed");
        _assetsRepo.Verify(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()), Times.Never);
        _uow.SaveCallCount.Should().Be(1);
    }
}
