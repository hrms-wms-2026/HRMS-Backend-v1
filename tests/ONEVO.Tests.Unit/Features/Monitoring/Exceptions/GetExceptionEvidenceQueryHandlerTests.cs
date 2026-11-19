using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Helpers;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using DomainException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class GetExceptionEvidenceQueryHandlerTests
{
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IExceptionScopeResolver> _scope = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<IFaceVerificationAttemptRepository> _faceChecks = new();
    private readonly Mock<ICheckInRepository> _checkIns = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero) };
    private readonly Mock<IDeviceStateSnapshotRepository> _deviceStates = new();
    private readonly Mock<IActivityDailySummaryRepository> _dailySummaries = new();
    private readonly Mock<IProductivityReportRepository> _aggregates = new();

    private static ProductivityAggregate Aggregate(decimal averageScore = 0m, int workedMinutes = 0, int dayCount = 0) =>
        new(0, 0, 0, 0, 0, 0, averageScore, workedMinutes, 0, dayCount);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Employee _employee;
    private readonly Guid _photoFileId = Guid.NewGuid();

    public GetExceptionEvidenceQueryHandlerTests()
    {
        _employee = new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(), LegalEntityId = _legalEntityId };
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.Setup(u => u.HasPermission(GetExceptionEvidenceQueryHandler.MonitoringReadPermission)).Returns(true);
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_employee);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, Timezone = "Asia/Colombo" });
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Guid.NewGuid(), [_employee.Id]));
        _faceChecks.Setup(f => f.ListForEmployeeInRangeAsync(_tenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _checkIns.Setup(c => c.ListForUserInRangeAsync(_tenantId, _employee.UserId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _deviceStates.Setup(d => d.ListForEmployeeInRangeAsync(_tenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _dailySummaries.Setup(s => s.GetRangeAsync(_tenantId, _employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _attendance.Setup(a => a.ListRecordsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<AttendanceRecord>(), 0));
        _aggregates.Setup(a => a.GetEmployeeAggregateAsync(_tenantId, _employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Aggregate());
    }

    private GetExceptionEvidenceQueryHandler CreateSut() => new(
        _exceptions.Object, _currentUser.Object, _scope.Object, _employees.Object, _legalEntities.Object,
        _attendance.Object, _faceChecks.Object, _checkIns.Object, _fileStorage.Object, _clock,
        _deviceStates.Object, _dailySummaries.Object, _aggregates.Object);

    [Fact]
    public async Task AnyCase_ShowsThatDaysDeviceActivity_FromTheCompanyDayWindow()
    {
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.AttendanceIrregularity, _employee.Id,
            new ExceptionMetadata { WorkDate = target }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));
        var device = Guid.NewGuid();
        var nineAm = new DateTimeOffset(2026, 9, 15, 3, 30, 0, TimeSpan.Zero); // 09:00 Colombo
        DateTimeOffset? from = null, to = null;
        _deviceStates.Setup(d => d.ListForEmployeeInRangeAsync(_tenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, DateTimeOffset f, DateTimeOffset t, int _, CancellationToken _) => { from = f; to = t; })
            .ReturnsAsync([
                Sample(device, nineAm, idle: false),
                Sample(device, nineAm.AddMinutes(5), idle: true),
                Sample(device, nineAm.AddMinutes(10), idle: false)
            ]);

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        // The 15th in Colombo starts 18:30 UTC on the 14th.
        from.Should().Be(new DateTimeOffset(2026, 9, 14, 18, 30, 0, TimeSpan.Zero));
        to.Should().Be(new DateTimeOffset(2026, 9, 15, 18, 30, 0, TimeSpan.Zero));
        var activity = result.Value!.DeviceActivity!;
        activity.ActiveMinutes.Should().Be(5);
        activity.IdleMinutes.Should().Be(5);
        activity.DeviceIds.Should().Equal(device);
    }

    [Fact]
    public async Task LowActivityCase_ShowsAWeek_MarksTheThreeRuleDays_AndTheRuleThresholds()
    {
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.SustainedLowActivity, _employee.Id,
            new ExceptionMetadata { WorkDate = target }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));
        _dailySummaries.Setup(s => s.GetRangeAsync(_tenantId, _employee.Id, target.AddDays(-6), target, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Summary(target, 20m), Summary(target.AddDays(-1), 30m), Summary(target.AddDays(-2), 25m), Summary(target.AddDays(-5), 80m)
            ]);

        var pattern = (await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None)).Value!.Pattern!;

        pattern.From.Should().Be(target.AddDays(-6));
        pattern.Days.Should().HaveCount(7);
        pattern.Days.Where(d => d.InRule).Select(d => d.Date).Should().Equal(target.AddDays(-2), target.AddDays(-1), target);
        pattern.Days.Single(d => d.Date == target.AddDays(-5)).ActivityScore.Should().Be(80m);
        pattern.Measures.Should().Contain(m => m.Unit == "score" && m.Value == 40m);
        pattern.Measures.Should().Contain(m => m.Unit == "days" && m.Value == 3m);
        pattern.Measures.Should().Contain(m => m.Label.StartsWith("Average") && m.Value == 25m);
    }

    [Fact]
    public async Task IrregularityCase_ShowsTheSameWeeklyFiguresTheJobCompared()
    {
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.AttendanceIrregularity, _employee.Id,
            new ExceptionMetadata { WorkDate = target }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));
        _aggregates.Setup(a => a.GetEmployeeAggregateAsync(_tenantId, _employee.Id, target.AddDays(-6), target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Aggregate(workedMinutes: 600, dayCount: 3));
        _aggregates.Setup(a => a.GetEmployeeAggregateAsync(_tenantId, _employee.Id, target.AddDays(-34), target.AddDays(-7), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Aggregate(workedMinutes: 9600, dayCount: 20));

        var pattern = (await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None)).Value!.Pattern!;

        pattern.Days.Should().HaveCount(14);
        pattern.Days.Count(d => d.InRule).Should().Be(7);
        // No figures stored on this (older) case: recomputed now, and flagged as such.
        pattern.MeasuresRecalculated.Should().BeTrue();
        pattern.Measures.Select(m => m.Value).Should().Equal(600m, 2400m, 1200m);
    }

    [Fact]
    public async Task WithoutMonitoringRead_DeviceActivityAndDailyActivityAreHidden_ButTheReasonAndAttendanceStay()
    {
        _currentUser.Setup(u => u.HasPermission(GetExceptionEvidenceQueryHandler.MonitoringReadPermission)).Returns(false);
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.SustainedLowActivity, _employee.Id, new ExceptionMetadata
        {
            WorkDate = target,
            Measures = ExceptionPatternMeasures.SustainedLowActivity([20m, 30m, 25m])
        }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));
        _dailySummaries.Setup(s => s.GetRangeAsync(_tenantId, _employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Summary(target, 20m)]);
        _attendance.Setup(a => a.ListRecordsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { new AttendanceRecord { Date = target, WorkedMinutes = 470, Status = "present" } }, 1));

        var evidence = (await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None)).Value!;

        evidence.ActivityHidden.Should().BeTrue();
        evidence.DeviceActivity.Should().BeNull();
        _deviceStates.Verify(d => d.ListForEmployeeInRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        var day = evidence.Pattern!.Days.Single(d => d.Date == target);
        day.ActivityScore.Should().BeNull();
        day.ActiveMinutes.Should().BeNull();
        day.WorkedMinutes.Should().Be(470);
        evidence.Pattern.Measures.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StoredFigures_AreShownAsFlagged_EvenIfTheDataChangedSince()
    {
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.AttendanceIrregularity, _employee.Id, new ExceptionMetadata
        {
            WorkDate = target,
            Measures = ExceptionPatternMeasures.AttendanceIrregularity(600, 2400)
        }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));
        // An attendance correction since then would change the recomputed week.
        _aggregates.Setup(a => a.GetEmployeeAggregateAsync(_tenantId, _employee.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Aggregate(workedMinutes: 2300, dayCount: 5));

        var pattern = (await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None)).Value!.Pattern!;

        pattern.MeasuresRecalculated.Should().BeFalse();
        pattern.Measures.Select(m => m.Value).Should().Equal(600m, 2400m, 1200m);
        _aggregates.Verify(a => a.GetEmployeeAggregateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IdentityCase_HasNoPattern()
    {
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id,
            new ExceptionMetadata { OccurredAt = _clock.UtcNow }, _clock.UtcNow);

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.Value!.Pattern.Should().BeNull();
        result.Value.DeviceActivity.Should().NotBeNull();
    }

    private DeviceStateSnapshot Sample(Guid device, DateTimeOffset at, bool idle) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employee.Id, AgentDeviceId = device,
        CapturedAt = at, IsIdle = idle, IdleSeconds = idle ? 300 : 0
    };

    private ActivityDailySummary Summary(DateOnly date, decimal score) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employee.Id, Date = date, ActivityScore = score
    };

    private DomainException Stored(ExceptionType type, Guid employeeId, ExceptionMetadata meta, DateTimeOffset detectedAt)
    {
        var @case = new DomainException
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employeeId, Type = type,
            Status = ExceptionStatus.Open, Title = "t", Description = "d", DetectedAt = detectedAt,
            MetadataJson = meta.ToJson()
        };
        _exceptions.Setup(e => e.GetByIdAsync(_tenantId, @case.Id, It.IsAny<CancellationToken>())).ReturnsAsync(@case);
        return @case;
    }

    [Fact]
    public async Task CaseOutsideTheReviewersScope_IsNotFound_AndNoPhotoIsSigned()
    {
        var @case = Stored(ExceptionType.IdentityAnomaly, Guid.NewGuid(),
            new ExceptionMetadata { PhotoFileId = _photoFileId }, _clock.UtcNow);

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.GetSignedUrlAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoAccess_IsForbidden()
    {
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id, new ExceptionMetadata(), _clock.UtcNow);
        _scope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task IdentityCase_UsesTheCompanyDay_SignsOnlyTheTriggeringPhoto_AndListsThatDaysChecks()
    {
        // 20:00 UTC on the 27th is 01:30 on the 28th in Colombo (UTC+5:30).
        var occurredAt = new DateTimeOffset(2026, 9, 27, 20, 0, 0, TimeSpan.Zero);
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id, new ExceptionMetadata
        {
            Source = ExceptionMetadata.SourceCheckInScan, OccurredAt = occurredAt, SimilarityScore = 21.5f,
            Reasons = ["not_matched"], PhotoFileId = _photoFileId
        }, occurredAt);
        var localDay = new DateOnly(2026, 9, 28);
        _attendance.Setup(a => a.GetRecordAsync(_tenantId, _employee.Id, localDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Date = localDay, ActualStart = occurredAt, AttendanceSource = "desktop_tray", Status = "present" });
        _faceChecks.Setup(f => f.ListForEmployeeInRangeAsync(_tenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FaceVerificationAttempt
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employee.Id, Purpose = "clock_in",
                Outcome = FaceVerificationAttempt.OutcomeFailed, FailureReason = "not_matched", CreatedAt = occurredAt
            }]);
        _checkIns.Setup(c => c.ListForUserInRangeAsync(_tenantId, _employee.UserId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EmployeeCheckIn
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _employee.UserId, CheckedInAt = occurredAt,
                DeviceSerialNumber = "SN-1",
                FaceScan = new MonitoringFaceScan { Status = MonitoringFaceScanStatus.NotMatched, SimilarityScore = 21.5f }
            }]);
        _fileStorage.Setup(f => f.GetSignedUrlAsync(_tenantId, _photoFileId, GetExceptionEvidenceQueryHandler.PhotoUrlExpiry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed/photo"));

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var evidence = result.Value!;
        evidence.WorkDate.Should().Be(localDay);
        evidence.Attendance!.Source.Should().Be("desktop_tray");
        evidence.Identity!.PhotoUrl.Should().Be("https://signed/photo");
        evidence.Identity.PhotoUrlExpiresAt.Should().Be(_clock.UtcNow + GetExceptionEvidenceQueryHandler.PhotoUrlExpiry);
        evidence.Identity.Reasons.Should().Equal("not_matched");
        evidence.FaceChecks.Should().ContainSingle(f => f.FailureReason == "not_matched");
        evidence.CheckInScans.Should().ContainSingle(s => s.ScanStatus == MonitoringFaceScanStatus.NotMatched && s.DeviceSerialNumber == "SN-1");
        _fileStorage.Verify(f => f.GetSignedUrlAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdentityCase_SignsEachFailedChecksPhoto_ReusingTheCasePhoto()
    {
        var at = _clock.UtcNow;
        var firstPhoto = Guid.NewGuid();
        var brokenPhoto = Guid.NewGuid();
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id, new ExceptionMetadata
        {
            Source = ExceptionMetadata.SourceFaceCheckOverride, OccurredAt = at, PhotoFileId = _photoFileId
        }, at);
        FaceVerificationAttempt Attempt(string outcome, Guid? photo) => new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employee.Id, Purpose = "clock_in",
            Outcome = outcome, FailureReason = "no_face_detected", PhotoFileId = photo, CreatedAt = at
        };
        _faceChecks.Setup(f => f.ListForEmployeeInRangeAsync(_tenantId, _employee.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Attempt(FaceVerificationAttempt.OutcomeFailed, firstPhoto),
                Attempt(FaceVerificationAttempt.OutcomeFailed, brokenPhoto),
                Attempt(FaceVerificationAttempt.OutcomeOverridden, _photoFileId),
                Attempt(FaceVerificationAttempt.OutcomePassed, null)
            ]);
        _fileStorage.Setup(f => f.GetSignedUrlAsync(_tenantId, _photoFileId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed/case"));
        _fileStorage.Setup(f => f.GetSignedUrlAsync(_tenantId, firstPhoto, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed/first"));
        _fileStorage.Setup(f => f.GetSignedUrlAsync(_tenantId, brokenPhoto, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        var checks = result.Value!.FaceChecks;
        checks.Select(c => c.PhotoUrl).Should().Equal("https://signed/first", null, "https://signed/case", null);
        checks.Select(c => c.PhotoUnavailable).Should().Equal(false, true, false, false);
        _fileStorage.Verify(f => f.GetSignedUrlAsync(_tenantId, _photoFileId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdentityCase_WithoutAKeptPhoto_HasNoUrl()
    {
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id,
            new ExceptionMetadata { Source = ExceptionMetadata.SourceFaceCheckOverride, OccurredAt = _clock.UtcNow }, _clock.UtcNow);

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.Value!.Identity!.PhotoUrl.Should().BeNull();
        result.Value.Identity.PhotoUnavailable.Should().BeFalse();
        _fileStorage.Verify(f => f.GetSignedUrlAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IdentityCase_PhotoKeptButCannotBeSigned_IsFlaggedUnavailable_AndTheRestStillLoads()
    {
        var @case = Stored(ExceptionType.IdentityAnomaly, _employee.Id,
            new ExceptionMetadata { OccurredAt = _clock.UtcNow, PhotoFileId = _photoFileId }, _clock.UtcNow);
        _fileStorage.Setup(f => f.GetSignedUrlAsync(_tenantId, _photoFileId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("R2 down"));

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Identity!.PhotoUrl.Should().BeNull();
        result.Value.Identity.PhotoUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task NightlyCase_UsesItsStoredTargetDate_AndHasNoIdentitySection()
    {
        var target = new DateOnly(2026, 9, 15);
        var @case = Stored(ExceptionType.AttendanceIrregularity, _employee.Id,
            new ExceptionMetadata { WorkDate = target }, new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero));

        var result = await CreateSut().Handle(new GetExceptionEvidenceQuery(@case.Id), CancellationToken.None);

        result.Value!.WorkDate.Should().Be(target);
        result.Value.Identity.Should().BeNull();
        result.Value.FaceChecks.Should().BeEmpty();
        _attendance.Verify(a => a.GetRecordAsync(_tenantId, _employee.Id, target, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OldNightlyCaseWithoutStoredDate_FallsBackToThePreviousUtcDay()
    {
        var @case = new DomainException
        {
            Type = ExceptionType.SustainedLowActivity, DetectedAt = new DateTimeOffset(2026, 9, 16, 23, 30, 0, TimeSpan.Zero)
        };

        GetExceptionEvidenceQueryHandler.ResolveWorkDate(@case, new ExceptionMetadata(), TimeZoneInfo.Utc)
            .Should().Be(new DateOnly(2026, 9, 15));
    }
}
