using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class ValidateFacePhotoCommandHandlerTests
{
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IBiometricProfileRepository> _profiles = new();
    private readonly Mock<IFaceQualityService> _quality = new();
    private readonly Mock<IFaceMatchService> _faceMatch = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public ValidateFacePhotoCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(Guid.NewGuid());
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private ValidateFacePhotoCommandHandler CreateSut() => new(
        _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _fileStorage.Object, _profiles.Object, _quality.Object, _faceMatch.Object, _employeeIdentity.Object);

    private static ValidateFacePhotoCommand Cmd(string? purpose = null) =>
        new(new MemoryStream(new byte[] { 3 }), "image/jpeg", 3, purpose);

    private static FaceQualityOutcome PassQuality() => new(true, true, true, 70f, 99f);

    [Fact]
    public async Task QualityFail_DoesNotCallFaceMatch_AndCannotProceed()
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceQualityOutcome(false, true, true, 20f, 99f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanProceed.Should().BeFalse();
        result.Value.LightingOk.Should().BeFalse();
        result.Value.FaceVisible.Should().BeTrue();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailurePoorLighting);
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sunglasses_SetsObstructionFailure()
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceQualityOutcome(true, true, false, 70f, 99f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.NoSunglassesOrMask.Should().BeFalse();
        result.Value.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureSunglassesOrMask);
    }

    [Fact]
    public async Task NoFaceDetected_ReturnsNoFaceCode()
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceQualityOutcome(false, false, false, null, null, FaceCount: 0));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNoFaceDetected);
    }

    [Fact]
    public async Task MultipleFaces_ReturnsMultipleFacesCode()
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceQualityOutcome(false, false, false, null, null, FaceCount: 2));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureMultipleFaces);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FacePhotoValidationPurpose.ClockIn)]
    [InlineData(FacePhotoValidationPurpose.ClockOut)]
    public async Task NoReferencePhoto_OutsideEnrollment_BlocksAndNeverUploads(string? purpose)
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassQuality());
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);

        var result = await CreateSut().Handle(Cmd(purpose), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNoReferencePhoto);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoReferencePhoto_QualityPass_EnrollsCaptureAndCanProceed()
    {
        var uploadedId = Guid.NewGuid();
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassQuality());
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                uploadedId, _tenantId, "key", "clock-in-reference.jpg", "clock-in-reference.jpg",
                "image/jpeg", 3, "abc", "active", DateTimeOffset.UtcNow, _userId, null)));
        _profiles.Setup(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _profiles.Setup(p => p.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.Enrollment), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.IsMatch.Should().BeTrue();
        result.Value.SimilarityScore.Should().Be(100f);
        _profiles.Verify(p => p.AddAsync(It.Is<BiometricProfile>(x => x.ReferencePhotoFileId == uploadedId), It.IsAny<CancellationToken>()), Times.Once);
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoReferencePhoto_QualityPass_StorageFail_StillCanProceed()
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassQuality());
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("Cloudflare R2 is not configured.", 502));

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.Enrollment), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.LightingOk.Should().BeTrue();
        result.Value.FaceVisible.Should().BeTrue();
        result.Value.NoSunglassesOrMask.Should().BeTrue();
        _profiles.Verify(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QualityPassAndMatch_CanProceed()
    {
        var referenceFileId = Guid.NewGuid();
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassQuality());
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 93.4f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.IsMatch.Should().BeTrue();
        result.Value.SimilarityScore.Should().Be(93.4f);
        result.Value.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task QualityPassButNotMatched_CannotProceed()
    {
        var referenceFileId = Guid.NewGuid();
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassQuality());
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 12f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNotMatched);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
