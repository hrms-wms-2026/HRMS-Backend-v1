using Moq;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class ParticipantAvailabilityCheckerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly LegalEntity Entity = new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, Timezone = "Asia/Colombo",
        WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0), StandardWorkingDays = "[1,2,3,4,5]",
    };

    // Tuesday 2026-11-03, 10:00–11:00 Colombo: inside working hours, so only leave rows appear.
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset End = Start.AddHours(1);

    private readonly Mock<ILeaveRequestReadRepository> _leave = new();
    private readonly Mock<ILeaveCalendarHolidayProvider> _holidays = new();
    private readonly Mock<ILegalEntityRepository> _entities = new();
    private readonly Mock<ICalendarTimezoneResolver> _zones = new();
    private readonly Mock<ILeaveVisibilityScopeProvider> _scope = new();
    private readonly Mock<IPositionAssignmentRepository> _positions = new();

    private static Employee Person(string first, Guid? userId = null, Guid? departmentId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId ?? Guid.NewGuid(), FirstName = first, LastName = "Test",
        LegalEntityId = Entity.Id, DepartmentId = departmentId,
    };

    private static LeaveRequest LeaveFor(Employee e) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = e.Id, Status = "approved",
        StartAt = Start.AddHours(-2), EndAt = End.AddHours(2),
    };

    private ParticipantAvailabilityChecker BuildSut(EmployeeVisibilityScope? scope, params LeaveRequest[] leave)
    {
        _leave.Setup(x => x.ListApprovedCoveringAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(leave);
        _holidays.Setup(x => x.ListHolidaysAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _entities.Setup(x => x.GetByIdForTenantAsync(TenantId, Entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Entity);
        _zones.Setup(x => x.ResolveForUserAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Colombo");
        _scope.Setup(x => x.ResolveForCurrentUserAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new LeaveVisibilityScopeResolution(scope, scope is null ? LeaveVisibilityScopeFailure.NoLeaveReadPermission : null));
        _positions.Setup(x => x.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionAssignment>());
        return new ParticipantAvailabilityChecker(
            _leave.Object, _holidays.Object, _entities.Object, _zones.Object, _scope.Object, _positions.Object);
    }

    private static EmployeeVisibilityScope DepartmentScope() =>
        new(false, null, new HashSet<Guid>(), new HashSet<Guid> { DepartmentId }, new HashSet<Guid>());

    [Fact]
    public async Task Reads_leave_and_holidays_once_for_all_participants()
    {
        var a = Person("Ada");
        var b = Person("Bob");
        var sut = BuildSut(DepartmentScope());

        await sut.CheckAsync(TenantId, null, [a, b], Start, End, false, "Asia/Colombo", CancellationToken.None);

        _leave.Verify(x => x.ListApprovedCoveringAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        _holidays.Verify(x => x.ListHolidaysAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        _entities.Verify(x => x.GetByIdForTenantAsync(TenantId, Entity.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scope_failure_shows_busy_for_others_and_on_leave_for_self()
    {
        var me = Person("Me");
        var other = Person("Other");
        var sut = BuildSut(null, LeaveFor(me), LeaveFor(other));

        var rows = await sut.CheckAsync(TenantId, me.Id, [me, other], Start, End, false, "Asia/Colombo", CancellationToken.None);

        Assert.Equal("On leave", Assert.Single(rows, r => r.EmployeeId == me.Id).ConflictingEventTitle);
        Assert.Equal("Busy", Assert.Single(rows, r => r.EmployeeId == other.Id).ConflictingEventTitle);
    }

    [Fact]
    public async Task Participant_in_scope_sees_on_leave()
    {
        var covered = Person("Covered", departmentId: DepartmentId);
        var sut = BuildSut(DepartmentScope(), LeaveFor(covered));

        var rows = await sut.CheckAsync(TenantId, null, [covered], Start, End, false, "Asia/Colombo", CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(CalendarConflictKinds.Leave, row.Kind);
        Assert.Equal("On leave", row.ConflictingEventTitle);
    }

    [Fact]
    public async Task Employee_without_user_uses_request_timezone()
    {
        // 18:00–19:00 in Colombo is outside 09:00–17:00 there, but inside working hours in London (12:30–13:30).
        var noUser = Person("NoUser", userId: Guid.Empty);
        var sut = BuildSut(DepartmentScope());
        var start = new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.FromHours(5.5));

        var rows = await sut.CheckAsync(TenantId, null, [noUser], start, start.AddHours(1), false, "Europe/London", CancellationToken.None);

        Assert.Empty(rows);
        _zones.Verify(x => x.ResolveForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
