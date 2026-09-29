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
using ONEVO.Application.Features.Monitoring.Biometrics.Services;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;
using ExceptionType = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.ExceptionType;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

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
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionAlertRouter> _router = new();
    private readonly Mock<IExceptionAlertRouterFactory> _routerFactory = new();
    private readonly List<MonitoringException> _cases = [];
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

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
        // The resolved real Employee.Id is what the profile lookup must match - distinct from the
        // raw UserId so tests can tell whether the handler used the resolved value.
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
        _exceptions.Setup(e => e.AddAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()))
            .Callback<MonitoringException, CancellationToken>((c, _) => _cases.Add(c))
            .Returns(Task.CompletedTask);
        _routerFactory.Setup(f => f.CreateForTenant(_tenantId)).Returns(_router.Object);
    }

    private UploadFaceScanCommandHandler CreateSut() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _fileStorage.Object, _profiles.Object,
        new EnrolledFaceMatcher(_fileStorage.Object, _faceMatch.Object), _employeeIdentity.Object, _clock, _unitOfWork.Object,
        _exceptions.Object, _routerFactory.Object, _legalEntities.Object);

    /// <summary>An enrolled profile whose reference photo compares to the upload with the given result.</summary>
    private (EmployeeCheckIn CheckIn, Guid UploadedFileId) SetupScanComparing(bool isMatch)
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(isMatch, isMatch ? 95f : 21.5f));
        return (checkIn, uploadedFileId);
    }

    private Task<Result<FaceScanUploadResponseDto>> Upload(Guid checkInId) =>
        CreateSut().Handle(new UploadFaceScanCommand(checkInId, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3), CancellationToken.None);

    [Fact]
    public async Task NotMatched_OpensIdentityCase_AndAlertsThroughTheTenantPinnedRouter()
    {
        var (checkIn, uploadedFileId) = SetupScanComparing(isMatch: false);

        var result = await Upload(checkIn.Id);

        result.IsSuccess.Should().BeTrue();
        var identityCase = _cases.Should().ContainSingle().Subject;
        identityCase.Type.Should().Be(ExceptionType.IdentityAnomaly);
        identityCase.EmployeeId.Should().Be(_employeeId);
        var meta = ExceptionMetadata.Parse(identityCase.MetadataJson);
        meta.Source.Should().Be(ExceptionMetadata.SourceCheckInScan);
        meta.CheckInId.Should().Be(checkIn.Id);
        meta.PhotoFileId.Should().Be(uploadedFileId);
        meta.SimilarityScore.Should().Be(21.5f);
        _router.Verify(r => r.NotifyDetectedAsync(identityCase, It.IsAny<CancellationToken>()), Times.Once);
        // Once for the case on its own, once for the alert.
        _exceptions.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NotMatched_DedupesFromTheStartOfTheCompanyDay_NotForever()
    {
        var (checkIn, _) = SetupScanComparing(isMatch: false);
        _clock.UtcNow = new DateTimeOffset(2026, 9, 28, 20, 0, 0, TimeSpan.Zero);
        var legalEntityId = Guid.NewGuid();
        _device.Setup(d => d.LegalEntityId).Returns(legalEntityId);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, Timezone = "Asia/Colombo" });
        DateTimeOffset? since = null;
        _exceptions.Setup(e => e.HasUnresolvedSinceAsync(_tenantId, _employeeId, ExceptionType.IdentityAnomaly, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, ExceptionType _, DateTimeOffset s, CancellationToken _) => since = s)
            .ReturnsAsync(false);

        await Upload(checkIn.Id);

        // 20:00 UTC on the 28th is 01:30 on the 29th in Colombo; that day starts 18:30 UTC on the 28th.
        since.Should().Be(new DateTimeOffset(2026, 9, 28, 18, 30, 0, TimeSpan.Zero));
        _cases.Should().ContainSingle();
    }

    [Fact]
    public async Task Verified_OpensNoCase()
    {
        var (checkIn, _) = SetupScanComparing(isMatch: true);

        await Upload(checkIn.Id);

        _cases.Should().BeEmpty();
        _router.Verify(r => r.NotifyDetectedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotMatched_WhileAnIdentityCaseFromTheSameDayIsStillOpen_AddsNoCaseAndNoAlert()
    {
        var (checkIn, _) = SetupScanComparing(isMatch: false);
        _exceptions.Setup(e => e.HasUnresolvedSinceAsync(_tenantId, _employeeId, ExceptionType.IdentityAnomaly, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Upload(checkIn.Id);

        result.IsSuccess.Should().BeTrue();
        _cases.Should().BeEmpty();
        _router.Verify(r => r.NotifyDetectedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotMatched_AlertRoutingFails_CaseIsStillSaved_AndUploadSucceeds()
    {
        var (checkIn, _) = SetupScanComparing(isMatch: false);
        _router.Setup(r => r.NotifyDetectedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("resolver blew up"));

        var result = await Upload(checkIn.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.NotMatched);
        _cases.Should().ContainSingle();
        _exceptions.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotMatched_CaseSaveFails_UploadStillSucceeds_AndNobodyIsAlerted()
    {
        var (checkIn, _) = SetupScanComparing(isMatch: false);
        _exceptions.Setup(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Upload(checkIn.Id);

        result.IsSuccess.Should().BeTrue();
        _router.Verify(r => r.NotifyDetectedAsync(It.IsAny<MonitoringException>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
                "image/jpeg", 3, "checksum", "available", DateTimeOffset.UtcNow, _userId, null)));

        return (checkIn, uploadedFileId);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_AndFacesMatch_SetsVerifiedWithSimilarity()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
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
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
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
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
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
    public async Task ProfileHasReferencePhoto_ButFaceMatchThrows_SetsFailed()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no face detected"));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.Failed);
        result.Value!.SimilarityScore.Should().BeNull();
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_ButOpenReadFails_SetsFailed_WithoutCallingFaceMatch()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
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

    [Fact]
    public async Task ProfileHasReferencePhoto_ButOpenReadThrows_SetsFailed_WithoutCallingFaceMatch()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("R2 request timed out"));
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
