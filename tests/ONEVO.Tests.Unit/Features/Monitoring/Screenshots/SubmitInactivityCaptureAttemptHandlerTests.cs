using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class SubmitInactivityCaptureAttemptHandlerTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IEvidenceAssetRepository> _assetsRepo = new();
    private readonly Mock<IInactivityCaptureAttemptRepository> _attemptsRepo = new();
    private readonly Mock<ITrayActivationRepository> _trayRepo = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();

    private static readonly DateTimeOffset IdleStart = DateTimeOffset.Parse("2026-08-10T01:00:00Z");
    private static readonly DateTimeOffset PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z");
    private static readonly DateTimeOffset DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z");
    private static readonly DateTimeOffset CapturedAt = DateTimeOffset.Parse("2026-08-10T01:05:05Z");

    public SubmitInactivityCaptureAttemptHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.UserId).Returns(_userId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                CompanySizeRange = "1-10",
                Status = TenantStatus.Active
            });

        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = _deviceId,
                TenantId = _tenantId,
                UserId = _userId,
                IsActive = true,
                ActivatedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });

        _attemptsRepo.Setup(r => r.FindContainingWorkSessionAsync(
                _tenantId, _userId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        EnableAllCaptureToggles();
    }

    private void EnableAllCaptureToggles()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.AutoScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private SubmitInactivityCaptureAttemptCommandHandler CreateHandler() => new(
        _fileStorage.Object,
        _assetsRepo.Object,
        _attemptsRepo.Object,
        _trayRepo.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _tenantSwitcher.Object,
        _clock,
        _uow,
        NullLogger<SubmitInactivityCaptureAttemptCommandHandler>.Instance);

    private SubmitInactivityCaptureAttemptCommand CapturedCommand() => new(
        _attemptId,
        "policy-7",
        IdleStart,
        PromptedAt,
        DecisionAt,
        CapturedAt,
        300,
        2,
        InactivityCaptureOutcomes.Captured,
        null,
        "image/jpeg",
        "deadbeef",
        -1920,
        0,
        3840,
        1080,
        "shot.jpg",
        128,
        new MemoryStream([0xFF, 0xD8, 0xFF]));

    private SubmitInactivityCaptureAttemptCommand DeclinedCommand() => new(
        _attemptId,
        "policy-7",
        IdleStart,
        PromptedAt,
        DecisionAt,
        null,
        300,
        0,
        InactivityCaptureOutcomes.Declined,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private FileRecordDto MakeFileRecord(Guid id) => new(
        id, _tenantId, $"tenants/{_tenantId}/files/{id}/shot.jpg", "shot.jpg", "shot.jpg",
        "image/jpeg", 128, "checksum", "available", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_Captured_UploadsOnce_CreatesInactivityApprovedAsset()
    {
        var fileRecordId = Guid.NewGuid();
        _attemptsRepo.Setup(r => r.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InactivityCaptureAttempt?)null);

        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, "shot.jpg", "image/jpeg",
                UploadPurposeCatalog.MonitoringScreenshot, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(MakeFileRecord(fileRecordId)));

        MonitoringEvidenceAsset? savedAsset = null;
        _assetsRepo.Setup(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()))
            .Callback<MonitoringEvidenceAsset>(a => savedAsset = a);

        InactivityCaptureAttempt? savedAttempt = null;
        _attemptsRepo.Setup(r => r.AddAsync(It.IsAny<InactivityCaptureAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<InactivityCaptureAttempt, CancellationToken>((a, _) => savedAttempt = a)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(CapturedCommand(), default);

        result.IsSuccess.Should().BeTrue();
        savedAsset.Should().NotBeNull();
        savedAsset!.TriggerType.Should().Be("inactivity_approved");
        savedAsset.FileRecordId.Should().Be(fileRecordId);
        savedAsset.EmployeeId.Should().Be(_userId);
        savedAsset.AgentDeviceId.Should().Be(_deviceId);
        savedAsset.MetadataJson.Should().Contain("deadbeef");
        savedAsset.MetadataJson.Should().Contain("virtual_bounds");

        savedAttempt.Should().NotBeNull();
        savedAttempt!.Id.Should().Be(_attemptId);
        savedAttempt.EvidenceAssetId.Should().Be(savedAsset.Id);
        savedAttempt.EmployeeId.Should().Be(_userId);
        savedAttempt.AgentDeviceId.Should().Be(_deviceId);

        result.Value!.EvidenceAssetId.Should().Be(savedAsset.Id);
        result.Value.FileRecordId.Should().Be(fileRecordId);
        _fileStorage.Verify(f => f.UploadAsync(
            _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
            UploadPurposeCatalog.MonitoringScreenshot, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Captured_WhenPolicyDisabled_Returns403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.AutoScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(CapturedCommand(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IdenticalRetry_ReturnsExistingIdsWithoutSecondUpload()
    {
        var fileRecordId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var existing = new InactivityCaptureAttempt
        {
            Id = _attemptId,
            TenantId = _tenantId,
            EmployeeId = _userId,
            AgentDeviceId = _deviceId,
            IdleStartedAt = IdleStart,
            PromptedAt = PromptedAt,
            DecisionAt = DecisionAt,
            CapturedAt = CapturedAt,
            IdleDurationSeconds = 300,
            MonitorCount = 2,
            Outcome = InactivityCaptureOutcomes.Captured,
            EvidenceAssetId = assetId,
            PolicyVersion = "policy-7",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _attemptsRepo.Setup(r => r.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _assetsRepo.Setup(r => r.GetByIdAsync(_tenantId, assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoringEvidenceAsset
            {
                Id = assetId,
                TenantId = _tenantId,
                EmployeeId = _userId,
                FileRecordId = fileRecordId,
                CapturedAt = CapturedAt,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var result = await CreateHandler().Handle(CapturedCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttemptId.Should().Be(_attemptId);
        result.Value.EvidenceAssetId.Should().Be(assetId);
        result.Value.FileRecordId.Should().Be(fileRecordId);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ConflictingRetry_Returns409()
    {
        var existing = new InactivityCaptureAttempt
        {
            Id = _attemptId,
            TenantId = _tenantId,
            EmployeeId = _userId,
            AgentDeviceId = _deviceId,
            IdleStartedAt = IdleStart,
            PromptedAt = PromptedAt,
            DecisionAt = DecisionAt,
            IdleDurationSeconds = 300,
            MonitorCount = 0,
            Outcome = InactivityCaptureOutcomes.Declined,
            PolicyVersion = "policy-7",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _attemptsRepo.Setup(r => r.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(CapturedCommand(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Be(SubmitInactivityCaptureAttemptCommandHandler.AttemptAlreadyRecordedCode);
    }

    [Fact]
    public async Task Handle_Declined_DoesNotRequirePolicyOrUpload()
    {
        _attemptsRepo.Setup(r => r.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InactivityCaptureAttempt?)null);
        _toggles.Setup(t => t.IsEnabledAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        InactivityCaptureAttempt? savedAttempt = null;
        _attemptsRepo.Setup(r => r.AddAsync(It.IsAny<InactivityCaptureAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<InactivityCaptureAttempt, CancellationToken>((a, _) => savedAttempt = a)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(DeclinedCommand(), default);

        result.IsSuccess.Should().BeTrue();
        savedAttempt!.Outcome.Should().Be(InactivityCaptureOutcomes.Declined);
        savedAttempt.EvidenceAssetId.Should().BeNull();
        result.Value!.EvidenceAssetId.Should().BeNull();
        result.Value.FileRecordId.Should().BeNull();
        _fileStorage.VerifyNoOtherCalls();
    }
}
