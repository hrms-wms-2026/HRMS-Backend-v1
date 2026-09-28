using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Services;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;
using ONEVO.Application.Features.Monitoring.CheckIn.Helpers;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
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
        _device.Object, _tenants.Object, _tenantSwitcher.Object, _profiles.Object, _quality.Object,
        new EnrolledFaceMatcher(_fileStorage.Object, _faceMatch.Object), _employeeIdentity.Object);

    private static ValidateFacePhotoCommand Cmd(string? purpose = null, string? pose = null) =>
        new(new MemoryStream(new byte[] { 3 }), "image/jpeg", 3, purpose, pose);

    private static FaceQualityOutcome PassQuality() =>
        new(true, true, true, 70f, 99f, Yaw: 2f, FacingFront: true);

    private static FaceQualityOutcome TurnedQuality(float yaw = 25f) =>
        new(true, true, true, 70f, 99f, Yaw: yaw, TurnedSideways: true);

    private void SetupQuality(FaceQualityOutcome quality) =>
        _quality.Setup(q => q.AnalyzeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quality);

    private void SetupNoProfile() =>
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);

    private void SetupProfile(Guid front, Guid? left = null, Guid? right = null)
    {
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Enrolled,
                ReferencePhotoFileId = front, LeftReferencePhotoFileId = left, RightReferencePhotoFileId = right
            });
        foreach (var id in new[] { (Guid?)front, left, right })
        {
            if (id is null) continue;
            _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, id.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Result<FileStreamDto>.Success(
                    new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        }
    }

    private void VerifyNothingSaved()
    {
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QualityFail_DoesNotCallFaceMatch_AndCannotProceed()
    {
        SetupQuality(new FaceQualityOutcome(false, true, true, 20f, 99f));

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
        SetupQuality(new FaceQualityOutcome(true, true, false, 70f, 99f));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.NoSunglassesOrMask.Should().BeFalse();
        result.Value.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureSunglassesOrMask);
    }

    [Fact]
    public async Task NoFaceDetected_ReturnsNoFaceCode()
    {
        SetupQuality(new FaceQualityOutcome(false, false, false, null, null, FaceCount: 0));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNoFaceDetected);
    }

    [Fact]
    public async Task MultipleFaces_ReturnsMultipleFacesCode()
    {
        SetupQuality(new FaceQualityOutcome(false, false, false, null, null, FaceCount: 2));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureMultipleFaces);
    }

    [Fact]
    public async Task Response_CarriesFaceCountAndBoxes_ForDiagnosis()
    {
        SetupQuality(new FaceQualityOutcome(false, false, false, null, null, FaceCount: 2,
            Faces: [new DetectedFace(99f, 0.3f, 0.2f, 0.4f, 0.5f), new DetectedFace(97f, 0.05f, 0.1f, 0.2f, 0.25f)]));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.FaceCount.Should().Be(2);
        result.Value.Faces.Should().HaveCount(2);
        result.Value.Faces![1].Left.Should().Be(0.05f);
        result.Value.Faces[1].Width.Should().Be(0.2f);
    }

    [Fact]
    public async Task GlassesGlare_ReturnsGlareCode()
    {
        SetupQuality(new FaceQualityOutcome(true, true, false, 70f, 99f, GlassesGlare: true));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureGlassesGlare);
    }

    [Fact]
    public async Task EyesClosed_ReturnsEyesClosedCode()
    {
        SetupQuality(new FaceQualityOutcome(true, false, true, 70f, 99f, EyesClosed: true));

        var result = await CreateSut().Handle(Cmd(), CancellationToken.None);

        result.Value!.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureEyesClosed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FacePhotoValidationPurpose.ClockIn)]
    [InlineData(FacePhotoValidationPurpose.ClockOut)]
    public async Task NoReferencePhoto_OutsideEnrollment_BlocksAndNeverUploads(string? purpose)
    {
        SetupQuality(PassQuality());
        SetupNoProfile();

        var result = await CreateSut().Handle(Cmd(purpose), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNoReferencePhoto);
        VerifyNothingSaved();
    }

    [Theory]
    [InlineData(FacePhotoPose.Front)]
    [InlineData(null)]
    public async Task Enrollment_FrontStep_NoReference_PassesButSavesNothing(string? pose)
    {
        SetupQuality(PassQuality());
        SetupNoProfile();

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.Enrollment, pose), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.FailureReason.Should().BeNull();
        VerifyNothingSaved();
    }

    [Theory]
    [InlineData(FacePhotoPose.Left)]
    [InlineData(FacePhotoPose.Right)]
    public async Task Enrollment_SideStep_TurnedHead_Passes(string pose)
    {
        SetupQuality(TurnedQuality());
        SetupNoProfile();

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.Enrollment, pose), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Enrollment_SideStep_LookingStraight_IsWrongPose()
    {
        SetupQuality(PassQuality());
        SetupNoProfile();

        var result = await CreateSut().Handle(
            Cmd(FacePhotoValidationPurpose.Enrollment, FacePhotoPose.Left), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureWrongPose);
    }

    [Fact]
    public async Task Enrollment_FrontStep_HeadTurned_IsWrongPose()
    {
        SetupQuality(TurnedQuality());
        SetupNoProfile();

        var result = await CreateSut().Handle(
            Cmd(FacePhotoValidationPurpose.Enrollment, FacePhotoPose.Front), CancellationToken.None);

        result.Value!.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureWrongPose);
    }

    [Fact]
    public async Task Enrollment_AlreadyEnrolled_SameFace_ProceedsAsAlreadyEnrolled()
    {
        SetupQuality(PassQuality());
        SetupProfile(Guid.NewGuid());
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 96f));

        var result = await CreateSut().Handle(
            Cmd(FacePhotoValidationPurpose.Enrollment, FacePhotoPose.Front), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.AlreadyEnrolled);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Enrollment_AlreadyEnrolled_DifferentFace_NotMatched()
    {
        SetupQuality(PassQuality());
        SetupProfile(Guid.NewGuid());
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 10f));

        var result = await CreateSut().Handle(
            Cmd(FacePhotoValidationPurpose.Enrollment, FacePhotoPose.Front), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNotMatched);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task QualityPassAndMatch_CanProceed()
    {
        SetupQuality(PassQuality());
        SetupProfile(Guid.NewGuid());
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 93.4f));

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.ClockIn), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.IsMatch.Should().BeTrue();
        result.Value.SimilarityScore.Should().Be(93.4f);
        result.Value.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task QualityPassButNotMatched_TriesAllReferences_CannotProceed()
    {
        SetupQuality(PassQuality());
        SetupProfile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 12f));

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.ClockIn), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureNotMatched);
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ClockIn_FrontMisses_SideReferenceMatches_CanProceed()
    {
        SetupQuality(PassQuality());
        SetupProfile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _faceMatch.SetupSequence(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 70f))
            .ReturnsAsync(new FaceMatchOutcome(true, 88f));

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.ClockIn), CancellationToken.None);

        result.Value!.CanProceed.Should().BeTrue();
        result.Value.SimilarityScore.Should().Be(88f);
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReferenceUnreadable_ReturnsVerificationFailed()
    {
        SetupQuality(PassQuality());
        var frontId = Guid.NewGuid();
        SetupProfile(frontId);
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, frontId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Failure("missing", 404));

        var result = await CreateSut().Handle(Cmd(FacePhotoValidationPurpose.ClockIn), CancellationToken.None);

        result.Value!.CanProceed.Should().BeFalse();
        result.Value.FailureReason.Should().Be(ValidateFacePhotoCommandHandler.FailureVerificationFailed);
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
