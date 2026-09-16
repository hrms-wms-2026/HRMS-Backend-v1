using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitInactivityCaptureAttempt;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class SubmitInactivityCaptureAttemptCommandHandlerTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IEvidenceAssetRepository> _assetsRepo = new();
    private readonly Mock<IInactivityCaptureAttemptRepository> _attemptsRepo = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();

    public SubmitInactivityCaptureAttemptCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.UserId).Returns(_userId);

        // The resolved real Employee.Id is what gets persisted - distinct from the raw UserId so
        // tests can tell whether the handler stored the resolved value or the JWT identity.
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId, Name = "Test Tenant", Slug = "test-tenant",
                CompanySizeRange = "1-10", Status = TenantStatus.Active
            });

        _attemptsRepo.Setup(a => a.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InactivityCaptureAttempt?)null);

        // Toggle/threshold resolution must keep receiving the raw UserId, never the resolved
        // EmployeeId - see ITrayEmployeeIdentityResolver's own doc comment on why.
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.AutoScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _toggles.Setup(t => t.GetIdleThresholdMinutesAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
    }

    private SubmitInactivityCaptureAttemptCommandHandler CreateHandler() => new(
        _fileStorage.Object,
        _assetsRepo.Object,
        _attemptsRepo.Object,
        _device.Object,
        _tenants.Object,
        _tenantSwitcher.Object,
        _toggles.Object,
        _employeeIdentity.Object,
        _clock,
        _uow,
        NullLogger<SubmitInactivityCaptureAttemptCommandHandler>.Instance);

    private SubmitInactivityCaptureAttemptCommand MakeCommand(string outcome, Stream? content = null) => new(
        _attemptId,
        "policy-v1",
        _clock.UtcNow.AddMinutes(-10),
        _clock.UtcNow.AddMinutes(-8),
        _clock.UtcNow.AddMinutes(-7),
        outcome == InactivityCaptureOutcomes.Captured ? _clock.UtcNow.AddMinutes(-6) : null,
        180,
        1,
        outcome,
        null,
        outcome == InactivityCaptureOutcomes.Captured ? "image/jpeg" : null,
        null,
        content);

    [Fact]
    public async Task Handle_Captured_UploadsScreenshotAndPersistsResolvedEmployeeId()
    {
        var fileRecordId = Guid.NewGuid();
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _employeeId, It.IsAny<string>(), "image/jpeg",
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                fileRecordId, _tenantId, "tenants/x/files/y/shot.jpg", "shot.jpg", "shot.jpg",
                "image/jpeg", 1024, "checksum", "available", DateTimeOffset.UtcNow, _employeeId, null)));

        MonitoringEvidenceAsset? savedAsset = null;
        _assetsRepo.Setup(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()))
            .Callback<MonitoringEvidenceAsset>(a => savedAsset = a);
        InactivityCaptureAttempt? savedAttempt = null;
        _attemptsRepo.Setup(r => r.Add(It.IsAny<InactivityCaptureAttempt>()))
            .Callback<InactivityCaptureAttempt>(a => savedAttempt = a);

        var result = await CreateHandler().Handle(
            MakeCommand(InactivityCaptureOutcomes.Captured, new MemoryStream(new byte[] { 1, 2, 3 })),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        savedAsset.Should().NotBeNull();
        savedAsset!.EmployeeId.Should().Be(_employeeId);
        savedAsset.TriggerType.Should().Be("inactivity_approved");
        savedAttempt.Should().NotBeNull();
        savedAttempt!.EmployeeId.Should().Be(_employeeId);
        savedAttempt.EvidenceAssetId.Should().Be(savedAsset.Id);
        _uow.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Declined_PersistsAttemptWithResolvedEmployeeId_WithoutUpload()
    {
        InactivityCaptureAttempt? savedAttempt = null;
        _attemptsRepo.Setup(r => r.Add(It.IsAny<InactivityCaptureAttempt>()))
            .Callback<InactivityCaptureAttempt>(a => savedAttempt = a);

        var result = await CreateHandler().Handle(
            MakeCommand(InactivityCaptureOutcomes.Declined), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        savedAttempt.Should().NotBeNull();
        savedAttempt!.EmployeeId.Should().Be(_employeeId);
        savedAttempt.EvidenceAssetId.Should().BeNull();
        _assetsRepo.Verify(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()), Times.Never);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyRecorded_ReturnsConflict()
    {
        _attemptsRepo.Setup(a => a.GetByIdAsync(_tenantId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InactivityCaptureAttempt { Id = _attemptId, TenantId = _tenantId, EmployeeId = _employeeId });

        var result = await CreateHandler().Handle(
            MakeCommand(InactivityCaptureOutcomes.Declined), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_PolicyDisabled_ReturnsForbidden()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(
            MakeCommand(InactivityCaptureOutcomes.Captured, new MemoryStream(new byte[] { 1 })), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_Unauthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateHandler().Handle(
            MakeCommand(InactivityCaptureOutcomes.Declined), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
