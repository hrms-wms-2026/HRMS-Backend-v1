using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class TeamAttendancePeriodReaderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    // Mon 26 Oct .. Sun 1 Nov 2026; today is Fri 30 Oct.
    private static readonly DateOnly From = new(2026, 10, 26), To = new(2026, 11, 1), Today = new(2026, 10, 30);

    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<ILeaveRequestReadRepository> _leaves = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _calendar = new();
    private readonly Mock<IPositionAssignmentRepository> _positions = new();
    private readonly Mock<IAttendanceAlertResolutionLookup> _alerts = new();
    private readonly Mock<IEmployeeClockInPolicyResolver> _policies = new();
    private readonly Mock<ILeaveVisibilityScopeProvider> _leaveVisibility = new();

    private readonly List<AttendanceRecord> _records = [];
    private readonly List<LeaveRequest> _approvedLeaves = [];
    private readonly Dictionary<Guid, Employee> _employeeRows = [];
    private readonly Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlySet<DateOnly>>> _excused = [];
    private EmployeeVisibilityScope? _leaveScope = new(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    public TeamAttendancePeriodReaderTests()
    {
        _clock.SetupGet(c => c.Today).Returns(Today);
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-10-30T12:00:00+00:00"));
        _attendance.Setup(a => a.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (_records.ToList(), _records.Count));
        _leaves.Setup(l => l.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _approvedLeaves.ToList());
        _calendar.Setup(c => c.ListHolidayDatesAsync(TenantId, From, To, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DateOnly>());
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _employeeRows);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity
            {
                Id = LegalEntityId, TenantId = TenantId, Timezone = "UTC", StandardWorkingDays = "[1,2,3,4,5]",
                WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0), BreakDurationMinutes = 60,
            });
        _positions.Setup(p => p.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionAssignment>());
        _alerts.Setup(a => a.ExcusedDatesForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _excused);
        _policies.Setup(p => p.ResolveThresholdsAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Employee>>(), To, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, IReadOnlyCollection<Employee> list, DateOnly _, CancellationToken _) =>
                list.ToDictionary(e => e.Id, _ => 0));
        _leaveVisibility.Setup(v => v.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LeaveVisibilityScopeResolution(_leaveScope, null));
    }

    private TeamAttendancePeriodReader Reader() => new(
        _attendance.Object, _leaves.Object, _legalEntities.Object, _clock.Object, _employees.Object, _calendar.Object,
        _positions.Object, _alerts.Object, _policies.Object, _leaveVisibility.Object);

    private Guid AddEmployee()
    {
        var id = Guid.NewGuid();
        _employeeRows[id] = new Employee { Id = id, TenantId = TenantId, LegalEntityId = LegalEntityId, HireDate = new DateOnly(2020, 1, 1) };
        return id;
    }

    private void Clock(Guid employeeId, DateOnly d, int startHour, int startMinute = 0, bool clockedOut = true) => _records.Add(new AttendanceRecord
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 0),
        ActualStart = new DateTimeOffset(d.Year, d.Month, d.Day, startHour, startMinute, 0, TimeSpan.Zero),
        ActualEnd = clockedOut ? new DateTimeOffset(d.Year, d.Month, d.Day, 17, 0, 0, TimeSpan.Zero) : null,
        WorkedMinutes = 420,
    });

    private void Leave(Guid employeeId, DateOnly d) => _approvedLeaves.Add(new LeaveRequest
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
        StartAt = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        EndAt = new DateTimeOffset(d.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero),
    });

    [Fact]
    public async Task Reader_ClassifiesEachEmployeeDay()
    {
        var a = AddEmployee();
        var b = AddEmployee();
        Clock(a, new DateOnly(2026, 10, 26), 9);       // on time
        Clock(a, new DateOnly(2026, 10, 27), 9, 30);   // late
        Leave(a, new DateOnly(2026, 10, 29));           // 28th: absent
        Clock(b, new DateOnly(2026, 10, 26), 8, 55);

        var result = await Reader().LoadAsync(TenantId, [a, b], From, To);

        var days = result.Single(r => r.EmployeeId == a).BucketByDate;
        days.Should().HaveCount(7);
        days[new DateOnly(2026, 10, 26)].Should().Be(TeamAttendanceBuckets.Present);
        days[new DateOnly(2026, 10, 27)].Should().Be(TeamAttendanceBuckets.Late);
        days[new DateOnly(2026, 10, 28)].Should().Be(TeamAttendanceBuckets.Absent);
        days[new DateOnly(2026, 10, 29)].Should().Be(TeamAttendanceBuckets.Leave);
        days[Today].Should().Be(TeamAttendanceBuckets.Off); // not yet due: the handler's today merge decides
        days[new DateOnly(2026, 10, 31)].Should().Be(TeamAttendanceBuckets.Off);
        var bDays = result.Single(r => r.EmployeeId == b).BucketByDate;
        bDays[new DateOnly(2026, 10, 26)].Should().Be(TeamAttendanceBuckets.Present);
        bDays[new DateOnly(2026, 10, 27)].Should().Be(TeamAttendanceBuckets.Absent);
    }

    [Fact]
    public async Task Reader_IssuesBatchedQueries()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => AddEmployee()).ToList();

        await Reader().LoadAsync(TenantId, ids, From, To);

        _attendance.Verify(a => a.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _leaves.Verify(l => l.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, It.IsAny<CancellationToken>()), Times.Once);
        _employees.Verify(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.ExcusedDatesForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), From, To, It.IsAny<CancellationToken>()), Times.Once);
        _legalEntities.Verify(l => l.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()), Times.Once);
        _policies.Verify(p => p.ResolveThresholdsAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Employee>>(), To, It.IsAny<CancellationToken>()), Times.Once);
        _attendance.Verify(a => a.ListBreaksAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reader_MasksLeaveAsAbsent_WhenNotLeaveAuthorized()
    {
        var a = AddEmployee();
        Leave(a, new DateOnly(2026, 10, 29));
        _leaveScope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        var result = await Reader().LoadAsync(TenantId, [a], From, To);

        result.Single().BucketByDate[new DateOnly(2026, 10, 29)].Should().Be(TeamAttendanceBuckets.Absent);
    }

    [Fact]
    public async Task Reader_NoLeaveScope_MasksLeaveAsAbsent()
    {
        var a = AddEmployee();
        Leave(a, new DateOnly(2026, 10, 29));
        _leaveScope = null;

        var result = await Reader().LoadAsync(TenantId, [a], From, To);

        result.Single().BucketByDate[new DateOnly(2026, 10, 29)].Should().Be(TeamAttendanceBuckets.Absent);
    }

    [Fact]
    public async Task Reader_LateWithMissingClockOut_IsLate()
    {
        var a = AddEmployee();
        Clock(a, new DateOnly(2026, 10, 27), 9, 45, clockedOut: false); // > 16h open: missing clock-out

        var result = await Reader().LoadAsync(TenantId, [a], From, To);

        result.Single().BucketByDate[new DateOnly(2026, 10, 27)].Should().Be(TeamAttendanceBuckets.Late);
    }

    [Fact]
    public async Task Reader_ExcusedLateAndAbsent_DropLikeTheOverview()
    {
        var a = AddEmployee();
        Clock(a, new DateOnly(2026, 10, 27), 9, 30);
        _excused[a] = new Dictionary<string, IReadOnlySet<DateOnly>>
        {
            [AttendanceAlertSignals.LateClockIns] = new HashSet<DateOnly> { new(2026, 10, 27) },
            [AttendanceAlertSignals.AbsentDays] = new HashSet<DateOnly> { new(2026, 10, 28) },
        };

        var days = (await Reader().LoadAsync(TenantId, [a], From, To)).Single().BucketByDate;

        days[new DateOnly(2026, 10, 27)].Should().Be(TeamAttendanceBuckets.Present);
        days[new DateOnly(2026, 10, 28)].Should().Be(TeamAttendanceBuckets.Off);
        days[new DateOnly(2026, 10, 26)].Should().Be(TeamAttendanceBuckets.Absent);
    }

    [Fact]
    public async Task Reader_EmptyIds_ReturnsEmpty_NoQueries()
    {
        var result = await Reader().LoadAsync(TenantId, [], From, To);

        result.Should().BeEmpty();
        _attendance.VerifyNoOtherCalls();
        _employees.VerifyNoOtherCalls();
    }
}
