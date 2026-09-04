using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class UploadFaceScanCommandHandlerTests
{
    private readonly Mock<ICheckInRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IBiometricProfileRepository> _profiles = new();
    private readonly Mock<IFaceMatchService> _faceMatch = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public UploadFaceScanCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(Guid.NewGuid());
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private UploadFaceScanCommandHandler CreateSut() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _fileStorage.Object, _profiles.Object, _faceMatch.Object, _clock, _unitOfWork.Object);

    private (EmployeeCheckIn CheckIn, Guid UploadedFileId) SetupSuccessfulUploadPath()
    {
        var checkIn = new EmployeeCheckIn { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId };
        var uploadedFileId = Guid.NewGuid();

        _repository.Setup(r => r.FindCheckInAsync(checkIn.Id, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIn);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.MonitoringFaceScan, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                uploadedFileId, _tenantId, "tenants/x/files/y/scan.jpg", "scan.jpg", "scan.jpg",
                "image/jpeg", 3, "checksum", "available", DateTimeOffset.UtcNow)));

        return (checkIn, uploadedFileId);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_AndFacesMatch_SetsVerifiedWithSimilarity()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 93.4f));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.Verified);
        result.Value!.SimilarityScore.Should().Be(93.4f);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_ButFacesDoNotMatch_SetsNotMatched()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 12.1f));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.NotMatched);
        result.Value!.SimilarityScore.Should().Be(12.1f);
    }

    [Fact]
    public async Task NoBiometricProfile_SetsNoReferencePhoto_WithoutCallingFaceMatch()
    {
        var (checkIn, _) = SetupSuccessfulUploadPath();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.NoReferencePhoto);
        result.Value!.SimilarityScore.Should().BeNull();
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_ButOpenReadFails_SetsFailed_WithoutCallingFaceMatch()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Failure("Reference photo not found.", 404));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.Failed);
        result.Value!.SimilarityScore.Should().BeNull();
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
