using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceTodayLeaveAwareTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Today_ApprovedFullDayLeaveWithoutClockInReturnsOnTimeOffWithoutWarning()
    {
        var fixture = CreateFixture(approvedLeave: true);

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttendanceStatus.Should().Be(AttendanceRecord.StatusOnTimeOff);
        result.Value.AttendanceStatusLabel.Should().Be("On time off");
        result.Value.ShouldHaveClockedIn.Should().BeFalse();
        result.Value.AttentionType.Should().BeNull();
    }

    [Fact]
    public async Task Today_ApprovedLeaveWithClockInReturnsWorkedDuringTimeOff()
    {
        var fixture = CreateFixture(
            approvedLeave: true,
            attendanceRecord: new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                Date = WorkDate, ExpectedWorkingDay = true,
                ScheduledStart = new(9, 0), ScheduledEnd = new(17, 0),
                ActualStart = UtcNow.AddHours(-1), Status = AttendanceRecord.StatusActive
            });

        var result = await fixture.Service.GetTodayAsync();

        result.Value!.AttendanceStatus.Should().Be(AttendanceRecord.StatusWorkedDuringTimeOff);
        result.Value.AttentionType.Should().Be("worked_during_time_off");
        result.Value.CanClockOut.Should().BeTrue();
    }

    [Fact]
    public async Task Today_NonWorkingDayWithoutClockInReturnsNonWorkingDayWithoutWarning()
    {
        var fixture = CreateFixture(
            now: new DateTimeOffset(2026, 8, 23, 5, 0, 0, TimeSpan.Zero));

        var result = await fixture.Service.GetTodayAsync();

        result.Value!.AttendanceStatus.Should().Be(AttendanceRecord.StatusNonWorkingDay);
        result.Value.ShouldHaveClockedIn.Should().BeFalse();
        result.Value.AttentionType.Should().BeNull();
        result.Value.CanClockIn.Should().BeTrue();
    }

    private static Fixture CreateFixture(
        bool approvedLeave = false,
        AttendanceRecord? attendanceRecord = null,
        DateTimeOffset? now = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var employee = new Employee
        {
            Id = EmployeeId, UserId = UserId, TenantId = TenantId,
            LegalEntityId = LegalEntityId, WorkModeId = 1
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
        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClockInPolicy
            {
                Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
                ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1),
                RemoteWebEnabled = true
            }]);
        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendanceRecord);
        attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));
        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        expectedWorkAreas.Setup(x => x.ResolveAsync(It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(
                new ExpectedWorkAreaResolution("remote", "UTC", "active_employee_work_mode")));
        var leaves = new Mock<ILeaveRequestReadRepository>();
        leaves.Setup(x => x.ListApprovedCoveringAsync(
                TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(approvedLeave
                ? [new LeaveRequest
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                    StartDate = WorkDate, EndDate = WorkDate, Status = "approved"
                }]
                : []);
        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupGet(x => x.UtcNow).Returns(now ?? UtcNow);

        return new Fixture(
            new AttendanceTodayStateService(
                currentUser.Object, dateTime.Object, employees.Object, legalEntities.Object,
                policies.Object, attendance.Object, authority.Object, expectedWorkAreas.Object, leaves.Object));
    }

    private sealed record Fixture(AttendanceTodayStateService Service);
}
