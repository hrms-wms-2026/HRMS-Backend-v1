using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.EnrollFacePhotos;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class EnrollFacePhotosCommandHandlerTests
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

    // Each photo carries its own byte so the quality mock can tell them apart.
    private const byte FrontByte = 1, LeftByte = 2, RightByte = 3;

    public EnrollFacePhotosCommandHandlerTests()
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
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);
        _profiles.Setup(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _profiles.Setup(p => p.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 95f));
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<FileRecordDto>.Success(new FileRecordDto(
                Guid.NewGuid(), _tenantId, "key", "f.jpg", "f.jpg",
                "image/jpeg", 3, "abc", "active", DateTimeOffset.UtcNow, _userId, null)));

        SetupQualities(
            front: Front(),
            left: Side(-25f),
            right: Side(25f));
    }

    private EnrollFacePhotosCommandHandler CreateSut() => new(
        _device.Object, _tenants.Object, _tenantSwitcher.Object, _fileStorage.Object,
        _profiles.Object, _quality.Object, _faceMatch.Object, _employeeIdentity.Object);

    private static EnrollFacePhotosCommand Cmd() => new(
        new FaceSetupPhoto(new MemoryStream([FrontByte]), "image/jpeg", 1),
        new FaceSetupPhoto(new MemoryStream([LeftByte]), "image/jpeg", 1),
        new FaceSetupPhoto(new MemoryStream([RightByte]), "image/jpeg", 1));

    private static FaceQualityOutcome Front() => new(true, true, true, 70f, 99f, Yaw: 3f, FacingFront: true);
    private static FaceQualityOutcome Side(float yaw) => new(true, true, true, 70f, 99f, Yaw: yaw, TurnedSideways: true);

    private void SetupQualities(FaceQualityOutcome front, FaceQualityOutcome left, FaceQualityOutcome right)
    {
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((s, _) =>
            {
                s.Position = 0;
                return Task.FromResult(s.ReadByte() switch
                {
                    FrontByte => front,
                    LeftByte => left,
                    _ => right
                });
            });
    }

    private void VerifyNothingSaved()
    {
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ThreeGoodPhotos_SavesAllThreeReferences()
    {
        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeTrue();
        _fileStorage.Verify(f => f.UploadAsync(
            _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
            UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        _profiles.Verify(p => p.AddAsync(It.Is<BiometricProfile>(x =>
            x.EmployeeId == _employeeId
            && x.ReferencePhotoFileId != null
            && x.LeftReferencePhotoFileId != null
            && x.RightReferencePhotoFileId != null), It.IsAny<CancellationToken>()), Times.Once);
        // Same-person check: front vs left and front vs right.
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AlreadyEnrolled_RefusesAndSavesNothing()
    {
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = Guid.NewGuid()
            });

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.AlreadyEnrolled);
        VerifyNothingSaved();
        _quality.Verify(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SidePhotosTurnedSameWay_FailsRightPhoto()
    {
        SetupQualities(Front(), Side(25f), Side(22f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
        result.Value.FailedPhoto.Should().Be("right");
        result.Value.FailureReason.Should().Be(EnrollFacePhotosCommandHandler.FailureSameSide);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task FrontPhotoNotLookingStraight_FailsFrontAsWrongPose()
    {
        SetupQualities(Side(20f), Side(-25f), Side(25f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.FailedPhoto.Should().Be("front");
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureWrongPose);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task LeftPhotoWithSunglasses_FailsLeftPhoto()
    {
        SetupQualities(Front(), new FaceQualityOutcome(true, true, false, 70f, 99f, Yaw: -25f, TurnedSideways: true), Side(25f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.FailedPhoto.Should().Be("left");
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureSunglassesOrMask);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task SidePhotoOfDifferentPerson_FailsThatPhoto()
    {
        _faceMatch.SetupSequence(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 94f))
            .ReturnsAsync(new FaceMatchOutcome(false, 8f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.FailedPhoto.Should().Be("right");
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNotMatched);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task UploadFails_ReportsVerificationFailedAndSavesNoProfile()
    {
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("R2 down", 502));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.Enrolled.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureVerificationFailed);
        _profiles.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.StatusCode.Should().Be(401);
    }
}
