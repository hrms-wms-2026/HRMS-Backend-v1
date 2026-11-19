using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.Queries;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.Team;

/// <summary>Working / Break / Leave popup (2026-10-29 design, section 6.4): the team's working-hours week trend.</summary>
public sealed class GetTeamWorkingHoursQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-09-30T10:00:00+00:00"); // Wed 15:30 Colombo
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Wednesday = new(2026, 9, 30);

    private sealed class Fixture
    {
        public readonly Mock<ICurrentUser> CurrentUser = new();
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<IEmployeeAuthorityResolver> Authority = new();
        public readonly Mock<IAttendanceReadRepository> Attendance = new();
        public readonly Mock<ILeaveRequestReadRepository> Leave = new();
        public readonly Mock<ILeaveVisibilityScopeProvider> LeaveScope = new();
        public List<AttendanceRecord> Records = [];
        public List<LeaveRequest> LeaveRows = [];

        public Fixture(params Guid[] covered)
        {
            CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            CurrentUser.SetupGet(x => x.UserId).Returns(UserId);
            CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
            Employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = ActorId, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId });
            Employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, Employee>());
            Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, false, covered));
            Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (Records, Records.Count));
            Leave.Setup(x => x.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LeaveRows);
            LeaveScope.Setup(x => x.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoLeaveReadPermission));
        }

        public GetTeamWorkingHoursQueryHandler Handler()
        {
            var legalEntities = new Mock<ILegalEntityRepository>();
            legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LegalEntity
                {
                    Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo", StandardWorkingDays = "[1,2,3,4,5]",
                    WorkStartTime = new(9, 0), WorkEndTime = new(17, 30), BreakDurationMinutes = 60,
                });
            var positions = new Mock<IPositionAssignmentRepository>();
            positions.Setup(x => x.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, PositionAssignment>());
            var clock = new Mock<IDateTimeProvider>();
            clock.SetupGet(x => x.UtcNow).Returns(UtcNow);
            return new GetTeamWorkingHoursQueryHandler(CurrentUser.Object, Employees.Object, legalEntities.Object, Authority.Object,
                Attendance.Object, Leave.Object, positions.Object, LeaveScope.Object, clock.Object);
        }
    }

    private static AttendanceRecord Record(Guid employeeId, DateOnly date, int worked, int breaks, DateTimeOffset? start = null,
        DateTimeOffset? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Date = date, WorkedMinutes = worked, BreakMinutes = breaks,
        ActualStart = start ?? new DateTimeOffset(date.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero),
        ActualEnd = end ?? new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
    };

    private static LeaveRequest LeaveOn(Guid employeeId, DateOnly date) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = Guid.NewGuid(), Status = LeaveRequestStatuses.Approved,
        StartAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)),
        EndAt = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)),
    };

    [Fact]
    public async Task Seven_days_oldest_first()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(member) { Records = [Record(member, Monday, 480, 60), Record(member, Monday.AddDays(1), 300, 30)] };

        var result = await f.Handler().Handle(new GetTeamWorkingHoursQuery(null), default);

        var r = result.Value!;
        Assert.Equal((Monday, Monday.AddDays(6)), (r.WeekStart, r.WeekEnd));
        Assert.Equal(Enumerable.Range(0, 7).Select(i => Monday.AddDays(i)), r.Days.Select(d => d.Date));
        Assert.Equal((480, 60, 1), (r.Days[0].WorkedMinutes, r.Days[0].BreakMinutes, r.Days[0].PeopleWithRecord));
        Assert.Equal((300, 30), (r.Days[1].WorkedMinutes, r.Days[1].BreakMinutes));
        Assert.All(r.Days.Skip(3), d => Assert.Equal((0, 0, 0, 0), (d.WorkedMinutes, d.BreakMinutes, d.OnLeaveCount, d.PeopleWithRecord)));
    }

    [Fact]
    public async Task Leave_counted_only_for_authorized_subjects()
    {
        var authorized = Guid.NewGuid();
        var masked = Guid.NewGuid();
        var f = new Fixture(authorized, masked) { LeaveRows = [LeaveOn(authorized, Monday), LeaveOn(masked, Monday)] };
        f.LeaveScope.Setup(x => x.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveVisibilityScopeResolution(
                new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                {
                    CoveredEmployeeIds = new HashSet<Guid> { authorized },
                }, null));

        var result = await f.Handler().Handle(new GetTeamWorkingHoursQuery(Monday), default);

        Assert.Equal(1, result.Value!.Days[0].OnLeaveCount);
        Assert.Equal(0, result.Value.Days[1].OnLeaveCount);
    }

    [Fact]
    public async Task Leave_is_never_counted_without_a_leave_scope()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(member) { LeaveRows = [LeaveOn(member, Monday)] };

        var result = await f.Handler().Handle(new GetTeamWorkingHoursQuery(Monday), default);

        Assert.All(result.Value!.Days, d => Assert.Equal(0, d.OnLeaveCount));
    }

    [Fact]
    public async Task Open_record_counts_until_now()
    {
        var member = Guid.NewGuid();
        var clockIn = UtcNow.AddHours(-3);
        var f = new Fixture(member) { Records = [Record(member, Wednesday, 0, 20, start: clockIn, end: null)] };
        f.Records[0].ActualEnd = null;

        var result = await f.Handler().Handle(new GetTeamWorkingHoursQuery(null), default);

        Assert.Equal(180 - 20, result.Value!.Days[2].WorkedMinutes);
    }

    [Fact]
    public async Task The_caller_is_never_in_their_own_team()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(ActorId, member) { Records = [Record(ActorId, Monday, 480, 0), Record(member, Monday, 60, 0)] };

        await f.Handler().Handle(new GetTeamWorkingHoursQuery(Monday), default);

        f.Attendance.Verify(x => x.ListRecordsAsync(TenantId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(member)),
            Monday, Monday.AddDays(6), 0, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Forbidden_without_attendance_read()
    {
        var f = new Fixture();
        f.CurrentUser.Setup(x => x.HasPermission("attendance:read")).Returns(false);

        var result = await f.Handler().Handle(new GetTeamWorkingHoursQuery(null), default);

        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData(2026, 9, 28, true)]
    [InlineData(2026, 9, 29, false)]
    [InlineData(2026, 10, 4, false)]
    public void Week_start_not_monday_is_rejected(int year, int month, int day, bool valid)
    {
        var result = new GetTeamWorkingHoursQueryValidator().Validate(new GetTeamWorkingHoursQuery(new DateOnly(year, month, day)));

        Assert.Equal(valid, result.IsValid);
        Assert.True(new GetTeamWorkingHoursQueryValidator().Validate(new GetTeamWorkingHoursQuery(null)).IsValid);
    }
}
