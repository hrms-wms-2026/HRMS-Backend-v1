using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

public sealed class GetCoveredTeamTodayQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-09-30T10:00:00+00:00"); // 15:30 Colombo, a Wednesday

    [Fact]
    public async Task Forbidden_when_caller_lacks_attendance_read()
    {
        var f = CreateFixture();
        f.CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(false);

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NotFound_when_actor_has_no_legal_entity()
    {
        var f = CreateFixture();
        f.Employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = ActorId, UserId = UserId, TenantId = TenantId, LegalEntityId = null });

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Empty_population_returns_zeroed_summary_and_no_members()
    {
        var f = CreateFixture();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, []));

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Members);
        Assert.Equal(0, result.Value.Summary.Total);
        Assert.Equal(0, result.Value.TotalMembers);
    }

    [Fact]
    public async Task Strips_actor_id_even_when_the_resolver_returns_it()
    {
        var f = CreateFixture();
        var otherId = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [ActorId, otherId]));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalMembers);
        Assert.DoesNotContain(result.Value.Members, m => m.EmployeeId == ActorId);
        f.Attendance.Verify(x => x.ListRecordsForDateAsync(
            TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(otherId)),
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Maps_an_active_clocked_in_member_to_working_using_batched_identity()
    {
        var f = CreateFixture();
        var memberId = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [memberId]));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = memberId,
                ActualStart = DateTimeOffset.Parse("2026-09-30T04:00:00+00:00"), Status = AttendanceRecord.StatusOnTime,
            }]);
        f.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [memberId] = new(memberId, "Priya K", "EMP-002", "Engineer", "Product", Guid.NewGuid()),
            });

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        var member = Assert.Single(result.Value!.Members);
        Assert.Equal("working", member.Status);
        Assert.Equal("Working", member.StatusLabel);
        Assert.Equal("Priya K", member.DisplayName);
        Assert.Equal("Engineer", member.PositionTitle);
        Assert.False(member.IsLate);
        Assert.Equal(1, result.Value.Summary.Working);
    }

    [Fact]
    public async Task Late_arrival_sets_isLate_overlay_while_keeping_working_status()
    {
        var f = CreateFixture();
        var memberId = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [memberId]));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = memberId,
                ActualStart = DateTimeOffset.Parse("2026-09-30T04:00:00+00:00"), Status = AttendanceRecord.StatusLate,
            }]);

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        var member = Assert.Single(result.Value!.Members);
        Assert.Equal("working", member.Status);
        Assert.True(member.IsLate);
        Assert.Equal(1, result.Value.Summary.Late);
    }

    [Fact]
    public async Task Leave_unauthorized_viewer_sees_on_time_off_subject_masked_as_absent_with_no_leave_block()
    {
        var f = CreateFixture();
        var memberId = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [memberId]));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        f.LeaveRequests.Setup(x => x.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = memberId, LeaveTypeId = Guid.NewGuid(),
                StartAt = DateTimeOffset.Parse("2026-09-30T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"),
                Status = LeaveRequestStatuses.Approved,
            }]);
        // No leave-visibility provider wired -> ResolveForCurrentUserAsync is never even called; leaveScope stays null.

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        var member = Assert.Single(result.Value!.Members);
        Assert.Equal("absent", member.Status);
        Assert.Null(member.Leave);
        Assert.Equal(1, result.Value.Summary.Absent);
        Assert.Equal(0, result.Value.Summary.OnLeave);
    }

    [Fact]
    public async Task Leave_authorized_viewer_sees_on_leave_with_leave_type_name_and_end_date()
    {
        var f = CreateFixture();
        var memberId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [memberId]));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        f.LeaveRequests.Setup(x => x.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = memberId, LeaveTypeId = leaveTypeId,
                StartAt = DateTimeOffset.Parse("2026-09-30T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-10-01T04:00:00+00:00"),
                Status = LeaveRequestStatuses.Approved,
            }]);
        f.LeaveTypes.Setup(x => x.ListAsync(TenantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType { Id = leaveTypeId, TenantId = TenantId, Name = "Annual Leave" }]);
        f.LeaveVisibilityScope.Setup(x => x.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveVisibilityScopeResolution(EmployeeVisibilityScope.Unrestricted(), null));

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        var member = Assert.Single(result.Value!.Members);
        Assert.Equal("on_leave", member.Status);
        Assert.NotNull(member.Leave);
        Assert.Equal("Annual Leave", member.Leave!.LeaveTypeName);
        Assert.Equal(1, result.Value.Summary.OnLeave);
    }

    [Fact]
    public async Task Ordering_places_critical_attention_before_absent_before_late_before_name()
    {
        var f = CreateFixture();
        var critical = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var late = Guid.NewGuid();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, [critical, absent, late]));
        // No records at all -> not_clocked_in for everyone; ShouldHaveClockedIn is true at 15:30
        // Colombo local (well after the 09:00 scheduled start), so every unrecorded subject maps
        // to "absent" with a critical not_clocked_in attention - use late's own record to give it
        // a distinct "working + late" outcome instead.
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = late,
                ActualStart = DateTimeOffset.Parse("2026-09-30T04:00:00+00:00"), Status = AttendanceRecord.StatusLate,
            }]);
        f.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>
            {
                [critical] = new(critical, "A Critical", "E1", null, null, null),
                [absent] = new(absent, "B Absent", "E2", null, null, null),
                [late] = new(late, "C Late", "E3", null, null, null),
            });

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        // critical and absent both resolve to "absent" with a critical not_clocked_in attention
        // here (no record at all), so both rank ahead of the merely-late working member by
        // severity; between the two "absent" rows, alphabetical name order breaks the tie.
        Assert.Equal(new[] { "A Critical", "B Absent", "C Late" }, result.Value!.Members.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task Limit_caps_returned_members_but_summary_and_totalMembers_cover_whole_population()
    {
        var f = CreateFixture();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        f.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, ids));
        f.Attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.Select(id => new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = id,
                ActualStart = DateTimeOffset.Parse("2026-09-30T04:00:00+00:00"), Status = AttendanceRecord.StatusOnTime,
            }).ToList());

        var result = await f.Handler.Handle(new GetCoveredTeamTodayQuery(Limit: 2), CancellationToken.None);

        Assert.Equal(2, result.Value!.Members.Count);
        Assert.Equal(5, result.Value.TotalMembers);
        Assert.Equal(5, result.Value.Summary.Total);
        Assert.Equal(5, result.Value.Summary.Working);
    }

    private static Fixture CreateFixture()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        currentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);

        var actor = new Employee { Id = ActorId, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId };
        var legalEntity = new LegalEntity
        {
            Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo",
            StandardWorkingDays = "[1,2,3,4,5]", WorkStartTime = new(9, 0), WorkEndTime = new(17, 30),
            BreakDurationMinutes = 60,
        };

        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>());

        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(legalEntity);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, []));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.ListRecordsForDateAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        attendance.Setup(x => x.ListBreaksForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee>());

        var leaveRequests = new Mock<ILeaveRequestReadRepository>();
        leaveRequests.Setup(x => x.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClockInPolicy { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1) }]);

        var positionAssignments = new Mock<IPositionAssignmentRepository>();
        positionAssignments.Setup(x => x.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment>());

        var leaveTypes = new Mock<ILeaveTypeRepository>();
        leaveTypes.Setup(x => x.ListAsync(TenantId, true, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var leaveVisibilityScope = new Mock<ILeaveVisibilityScopeProvider>();
        leaveVisibilityScope.Setup(x => x.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoLeaveReadPermission));

        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupGet(x => x.UtcNow).Returns(UtcNow);

        var todayState = new Mock<IAttendanceTodayStateService>();

        var handler = new AttendanceReadHandler(
            currentUser.Object,
            employees.Object,
            attendance.Object,
            authority.Object,
            todayState.Object,
            leaveRequests: leaveRequests.Object,
            legalEntities: legalEntities.Object,
            dateTimeProvider: dateTime.Object,
            policies: policies.Object,
            positionAssignments: positionAssignments.Object,
            leaveTypes: leaveTypes.Object,
            leaveVisibilityScope: leaveVisibilityScope.Object);

        return new Fixture(
            handler, currentUser, employees, attendance, authority, leaveRequests, leaveTypes, leaveVisibilityScope);
    }

    private sealed record Fixture(
        AttendanceReadHandler Handler,
        Mock<ICurrentUser> CurrentUser,
        Mock<IEmployeeRepository> Employees,
        Mock<IAttendanceReadRepository> Attendance,
        Mock<IEmployeeAuthorityResolver> Authority,
        Mock<ILeaveRequestReadRepository> LeaveRequests,
        Mock<ILeaveTypeRepository> LeaveTypes,
        Mock<ILeaveVisibilityScopeProvider> LeaveVisibilityScope);
}
