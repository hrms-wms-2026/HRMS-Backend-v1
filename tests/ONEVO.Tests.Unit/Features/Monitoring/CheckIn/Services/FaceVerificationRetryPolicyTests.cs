using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Services;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Services;

public class FaceVerificationRetryPolicyTests
{
    private readonly Mock<IFaceVerificationAttemptRepository> _attempts = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Guid _hrUserId = Guid.NewGuid();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _photoFileId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 9, 27, 4, 30, 0, TimeSpan.Zero);
    private readonly List<FaceVerificationAttempt> _added = [];
    private List<FaceVerificationAttempt> _earlierFailures = [];

    public FaceVerificationRetryPolicyTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _attempts.Setup(a => a.AddAsync(It.IsAny<FaceVerificationAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<FaceVerificationAttempt, CancellationToken>((a, _) => _added.Add(a))
            .Returns(Task.CompletedTask);
        _attempts.Setup(a => a.GetConsecutiveFailuresAsync(
                _tenantId, _employeeId, "clock_in", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _earlierFailures);
        _attempts.Setup(a => a.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.MonitoringFaceScan, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                _photoFileId, _tenantId, "key", "f.jpg", "f.jpg", "image/jpeg", 3, "abc", "active",
                DateTimeOffset.UtcNow, _userId, null)));
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _employeeId, TenantId = _tenantId, FirstName = "Dapi", LastName = "Owner", LegalEntityId = _legalEntityId });
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                Guid.NewGuid(), _managerUserId, Guid.NewGuid(), FaceVerificationRetryPolicy.ReviewerPermission,
                EmployeeAuthorityPurpose.FaceVerificationOverrideReview, EmployeeApprovalRouteSource.ReportingLine, null)));
    }

    private FaceVerificationRetryPolicy CreateSut() => new(
        _attempts.Object, _fileStorage.Object, _employees.Object, _authority.Object,
        _notifications.Object, _clock.Object, _permissions.Object);

    private void SetupHrUsers(params Guid[] userIds) =>
        _permissions.Setup(p => p.ListUserIdsWithPermissionCodeAsync(
                _tenantId, FaceVerificationRetryPolicy.HrFallbackPermission, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

    private void VerifyAlerted(Guid userId, Times times) =>
        _notifications.Verify(n => n.SendTemplatedAsync(
            _tenantId, userId, FaceVerificationRetryPolicy.NotificationTemplate,
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            FaceVerificationRetryPolicy.RelatedEntityType, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), times);

    private FaceCheckAttemptContext Context(Guid? deviceLegalEntity = null) =>
        new(_tenantId, _userId, _employeeId, deviceLegalEntity, "clock_in");

    private static FacePhotoValidationResponseDto Failed(string reason) =>
        new(true, true, true, IsMatch: false, CanProceed: false, SimilarityScore: 12f, FailureReason: reason);

    private static FacePhotoValidationResponseDto Passed() =>
        new(true, true, true, IsMatch: true, CanProceed: true, SimilarityScore: 97f, FailureReason: null);

    private FaceVerificationAttempt EarlierFailure(string reason) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Purpose = "clock_in",
        Outcome = FaceVerificationAttempt.OutcomeFailed, FailureReason = reason, CreatedAt = _now.AddMinutes(-2)
    };

    private void VerifyNoAlert() =>
        _notifications.Verify(n => n.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Pass_IsRecorded_AndResetsCount()
    {
        var result = await CreateSut().ApplyAsync(Context(), Passed(), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeTrue();
        result.FailedAttempts.Should().Be(0);
        _added.Should().ContainSingle(a => a.Outcome == FaceVerificationAttempt.OutcomePassed);
        VerifyNoAlert();
    }

    [Fact]
    public async Task FirstFailure_StaysBlocked_Attempt1Of3()
    {
        var result = await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeFalse();
        result.FailureReason.Should().Be("not_matched");
        result.FailedAttempts.Should().Be(1);
        result.MaxAttempts.Should().Be(3);
        _added.Should().ContainSingle(a => a.Outcome == FaceVerificationAttempt.OutcomeFailed && a.FailureReason == "not_matched");
        VerifyNoAlert();
    }

    [Fact]
    public async Task SecondFailure_StaysBlocked_Attempt2Of3()
    {
        _earlierFailures = [EarlierFailure("face_not_visible")];

        var result = await CreateSut().ApplyAsync(Context(), Failed("poor_lighting"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeFalse();
        result.FailedAttempts.Should().Be(2);
        VerifyNoAlert();
    }

    [Fact]
    public async Task ThirdFailure_LetsThrough_KeepsPhoto_AlertsManager()
    {
        _earlierFailures = [EarlierFailure("face_not_visible"), EarlierFailure("not_matched")];

        var result = await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeTrue();
        result.FailureReason.Should().Be(FaceVerificationRetryPolicy.ManagerReview);
        result.FailedAttempts.Should().Be(3);

        var attempt = _added.Should().ContainSingle().Subject;
        attempt.Outcome.Should().Be(FaceVerificationAttempt.OutcomeOverridden);
        attempt.PhotoFileId.Should().Be(_photoFileId);

        _authority.Verify(a => a.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r =>
                r.SubjectEmployeeId == _employeeId
                && r.LegalEntityId == _legalEntityId
                && r.RequiredPermission == FaceVerificationRetryPolicy.ReviewerPermission
                && r.Purpose == EmployeeAuthorityPurpose.FaceVerificationOverrideReview),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendTemplatedAsync(
            _tenantId, _managerUserId, FaceVerificationRetryPolicy.NotificationTemplate,
            It.Is<IReadOnlyDictionary<string, string>>(p =>
                p["employeeName"] == "Dapi Owner"
                && p["action"] == "clock in"
                && p["reasons"].Contains("face did not match")
                && p["reasons"].Contains("face not fully visible")),
            FaceVerificationRetryPolicy.RelatedEntityType, attempt.Id, It.IsAny<CancellationToken>()), Times.Once);
        _attempts.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThirdFailure_UsesDeviceLegalEntityWhenKnown()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        var deviceLegalEntity = Guid.NewGuid();

        await CreateSut().ApplyAsync(Context(deviceLegalEntity), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        _authority.Verify(a => a.ResolveApproverAsync(
            It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == deviceLegalEntity), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThirdFailure_WithManager_DoesNotAlsoAlertHr()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        SetupHrUsers(_hrUserId);

        await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        VerifyAlerted(_managerUserId, Times.Once());
        VerifyAlerted(_hrUserId, Times.Never());
    }

    [Fact]
    public async Task ThirdFailure_NoManager_AlertsEveryHrUser_ButNotTheEmployee()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Failure("No approver", 404));
        var secondHr = Guid.NewGuid();
        // The owner often holds HR permissions too — they must not be alerted about themselves.
        SetupHrUsers(_hrUserId, secondHr, _userId);

        var result = await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeTrue();
        result.FailureReason.Should().Be(FaceVerificationRetryPolicy.ManagerReview);
        VerifyAlerted(_hrUserId, Times.Once());
        VerifyAlerted(secondHr, Times.Once());
        VerifyAlerted(_userId, Times.Never());
        _added.Should().ContainSingle(a => a.Outcome == FaceVerificationAttempt.OutcomeOverridden);
    }

    [Fact]
    public async Task ThirdFailure_NoManagerNoHr_StillLetsThrough_WithoutAlert()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        _authority.Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Failure("No approver", 404));
        SetupHrUsers(_userId);

        var result = await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeTrue();
        VerifyNoAlert();
    }

    [Fact]
    public async Task ThirdFailure_EmployeeRecordMissing_AlertsHr()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        SetupHrUsers(_hrUserId);

        await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        VerifyAlerted(_hrUserId, Times.Once());
    }

    [Fact]
    public async Task ThirdFailure_PhotoUploadFails_StillLetsThrough()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];
        _fileStorage.Setup(f => f.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("R2 down", 502));

        var result = await CreateSut().ApplyAsync(Context(), Failed("not_matched"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeTrue();
        _added.Single().PhotoFileId.Should().BeNull();
    }

    [Fact]
    public async Task NoReferencePhoto_IsNotCounted_AndStaysBlocked()
    {
        _earlierFailures = [EarlierFailure("x"), EarlierFailure("y")];

        var result = await CreateSut().ApplyAsync(Context(), Failed("no_reference_photo"), new MemoryStream([1]), "image/jpeg", CancellationToken.None);

        result.CanProceed.Should().BeFalse();
        result.FailureReason.Should().Be("no_reference_photo");
        _added.Should().BeEmpty();
        VerifyNoAlert();
    }
}
