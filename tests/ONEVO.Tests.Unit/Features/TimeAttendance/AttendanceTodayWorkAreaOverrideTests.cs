using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

/// <summary>
/// End-to-end coverage of AttendanceTodayStateService using the real ExpectedWorkAreaResolver
/// (not a mock), proving that an approved one-day Work Area Change Request actually changes the
/// Today response's effective work area and Clock-in Policy branch - the Backend Part 2 gap.
/// </summary>
public sealed class AttendanceTodayWorkAreaOverrideTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
    private const int OnsiteWorkModeId = 1;
    private const int RemoteWorkModeId = 2;
    private const int HybridWorkModeId = 3;

    [Fact]
    public async Task Today_ApprovedRemoteOverride_OverridesPermanentOnsiteWorkModeAndUsesRemotePolicy()
    {
        var fixture = CreateFixture(
            permanentWorkModeId: OnsiteWorkModeId,
            approvedOverrideArea: "remote");
        fixture.Policy.OnsiteWebEnabled = false;
        fixture.Policy.OnsitePhotoRequired = true;
        fixture.Policy.RemoteWebEnabled = true;
        fixture.Policy.RemotePhotoRequired = false;
        fixture.Policy.LocationVerificationRequired = true;
        fixture.Policy.AllowedRadiusMeters = 150;

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWorkMode.Should().Be("remote");
        result.Value.ExpectedWorkAreaSource.Should().Be("approved_work_area_change_request");
        result.Value.AllowedClockInMethods.Web.Should().BeTrue();
        result.Value.AllowedClockInMethods.PhotoRequired.Should().BeFalse();
        result.Value.AllowedClockInMethods.LocationRequired.Should().BeTrue();
        result.Value.AllowedClockInMethods.AllowedRadiusMeters.Should().Be(150);
    }

    [Fact]
    public async Task Today_ApprovedOnsiteOverride_OverridesPermanentRemoteWorkModeAndUsesOnsitePolicy()
    {
        var fixture = CreateFixture(
            permanentWorkModeId: RemoteWorkModeId,
            approvedOverrideArea: "onsite");
        fixture.Policy.RemoteWebEnabled = false;
        fixture.Policy.OnsiteWebEnabled = true;
        fixture.Policy.OnsitePhotoRequired = true;

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWorkMode.Should().Be("onsite");
        result.Value.ExpectedWorkAreaSource.Should().Be("approved_work_area_change_request");
        result.Value.AllowedClockInMethods.Web.Should().BeTrue();
        result.Value.AllowedClockInMethods.PhotoRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Today_NoApprovedOverride_HybridEmployee_ResolvesToEitherAndUsesEitherPolicy()
    {
        var fixture = CreateFixture(
            permanentWorkModeId: HybridWorkModeId,
            approvedOverrideArea: null);
        fixture.Policy.EitherWebEnabled = true;
        fixture.Policy.EitherPhotoRequired = true;
        fixture.Policy.EitherLocationCheckRequired = true;

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWorkMode.Should().Be("hybrid");
        result.Value.ExpectedWorkAreaSource.Should().Be("active_employee_work_mode");
        result.Value.AllowedClockInMethods.Web.Should().BeTrue();
        result.Value.AllowedClockInMethods.PhotoRequired.Should().BeTrue();
        result.Value.AllowedClockInMethods.LocationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Today_NoApprovedOverride_FollowsPermanentWorkMode()
    {
        var fixture = CreateFixture(
            permanentWorkModeId: OnsiteWorkModeId,
            approvedOverrideArea: null);
        fixture.Policy.OnsiteWebEnabled = true;

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWorkMode.Should().Be("onsite");
        result.Value.ExpectedWorkAreaSource.Should().Be("active_employee_work_mode");
    }

    [Fact]
    public async Task Today_ExistingAttendanceRecord_UsesStoredSnapshotEvenWhenLiveResolutionDiffers()
    {
        // The employee already clocked in earlier today with the onsite fallback (no approved
        // override existed yet at that moment). An override for today has since been approved,
        // but Today must not contradict the historical snapshot already on the attendance record.
        var fixture = CreateFixture(
            permanentWorkModeId: OnsiteWorkModeId,
            approvedOverrideArea: "remote",
            attendanceRecord: new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                Date = WorkDate, ExpectedWorkingDay = true,
                ScheduledStart = new(9, 0), ScheduledEnd = new(17, 0),
                ExpectedWorkArea = AttendanceRecord.WorkAreaOnsite,
                ActualStart = UtcNow.AddHours(-1), Status = AttendanceRecord.StatusActive
            });

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpectedWorkMode.Should().Be("onsite");
        result.Value.ExpectedWorkAreaSource.Should().Be("attendance_record_snapshot");
    }

    private static Fixture CreateFixture(
        int permanentWorkModeId,
        string? approvedOverrideArea,
        AttendanceRecord? attendanceRecord = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var employee = new Employee
        {
            Id = EmployeeId, UserId = UserId, TenantId = TenantId,
            LegalEntityId = LegalEntityId, WorkModeId = permanentWorkModeId
        };
        var legalEntity = new LegalEntity
        {
            Id = LegalEntityId, TenantId = TenantId, Timezone = "UTC",
            StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = new(9, 0), WorkEndTime = new(17, 0),
            BreakDurationMinutes = 30
        };
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);

        var policy = new ClockInPolicy
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1)
        };
        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([policy]);

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendanceRecord);
        attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WorkMode { Id = OnsiteWorkModeId, Code = "onsite", Label = "On-site" },
                new WorkMode { Id = RemoteWorkModeId, Code = "remote", Label = "Remote" },
                new WorkMode { Id = HybridWorkModeId, Code = "hybrid", Label = "Hybrid" }
            ]);

        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupGet(x => x.UtcNow).Returns(UtcNow);

        var workAreaChangeRequests = new Mock<IWorkAreaChangeRequestRepository>();
        workAreaChangeRequests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(approvedOverrideArea is null
                ? null
                : new WorkAreaChangeRequest
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                    LegalEntityId = LegalEntityId, Date = WorkDate,
                    CurrentExpectedWorkArea = "onsite", RequestedWorkArea = approvedOverrideArea,
                    Reason = "Appointment", Status = WorkAreaChangeRequest.StatusApproved
                });

        var expectedWorkAreas = new ExpectedWorkAreaResolver(
            dateTime.Object, workModes.Object, workAreaChangeRequests.Object);

        var service = new AttendanceTodayStateService(
            currentUser.Object, dateTime.Object, employees.Object, legalEntities.Object,
            policies.Object, attendance.Object, authority.Object, expectedWorkAreas);

        return new Fixture(service, policy);
    }

    private sealed record Fixture(AttendanceTodayStateService Service, ClockInPolicy Policy);
}
