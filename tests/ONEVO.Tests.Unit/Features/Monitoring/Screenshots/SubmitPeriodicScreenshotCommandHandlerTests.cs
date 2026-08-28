using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class SubmitPeriodicScreenshotCommandHandlerTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IEvidenceAssetRepository> _assetsRepo = new();
    private readonly Mock<ITrayActivationRepository> _trayRepo = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SubmitPeriodicScreenshotCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.UserId).Returns(_userId);

        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = _deviceId, TenantId = _tenantId, UserId = _userId,
                IsActive = true, ActivatedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
            });

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId, Name = "Test Tenant", Slug = "test-tenant",
                CompanySizeRange = "1-10", Status = TenantStatus.Active
            });
    }

    private SubmitPeriodicScreenshotCommandHandler CreateHandler() => new(
        _fileStorage.Object,
        _assetsRepo.Object,
        _trayRepo.Object,
        _device.Object,
        _tenants.Object,
        _tenantSwitcher.Object,
        _clock,
        _uow,
        NullLogger<SubmitPeriodicScreenshotCommandHandler>.Instance);

    private FileRecordDto MakeFileRecord(Guid id) => new(
        id, _tenantId, $"tenants/{_tenantId}/files/{id}/shot.jpg", "shot.jpg", "shot.jpg",
        "image/jpeg", 1024, "checksum", "available", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_UploadFails_ReturnsFailureAndDoesNotCreateAsset()
    {
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.MonitoringScreenshot, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("storage_not_entitled", 403));

        var result = await CreateHandler().Handle(
            new SubmitPeriodicScreenshotCommand("shot.jpg", "image/jpeg", new MemoryStream(), DateTimeOffset.UtcNow),
            default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _assetsRepo.Verify(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()), Times.Never);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Success_CreatesPeriodicEvidenceAssetWithNoAgentCommand()
    {
        var fileRecordId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.MonitoringScreenshot, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(MakeFileRecord(fileRecordId)));

        MonitoringEvidenceAsset? savedAsset = null;
        _assetsRepo.Setup(r => r.Add(It.IsAny<MonitoringEvidenceAsset>()))
            .Callback<MonitoringEvidenceAsset>(a => savedAsset = a);

        var result = await CreateHandler().Handle(
            new SubmitPeriodicScreenshotCommand("shot.jpg", "image/jpeg", new MemoryStream(), capturedAt),
            default);

        result.IsSuccess.Should().BeTrue();
        savedAsset.Should().NotBeNull();
        savedAsset!.FileRecordId.Should().Be(fileRecordId);
        savedAsset.AgentCommandId.Should().BeNull();
        savedAsset.TriggerType.Should().Be("periodic");
        savedAsset.EvidenceType.Should().Be("screenshot");
        savedAsset.EmployeeId.Should().Be(_userId);
        savedAsset.CapturedAt.Should().Be(capturedAt);
        result.Value.Should().Be(savedAsset.Id);
        _uow.SaveCallCount.Should().Be(1);
    }
}
