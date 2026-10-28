using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CheckCalendarConflictsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();

    private CheckCalendarConflictsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _employees.Setup(x => x.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, FirstName = "Ada", LastName = "Lovelace" });
        _events.Setup(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new CheckCalendarConflictsQueryHandler(_currentUser.Object, _events.Object, _employees.Object, _expander.Object);
    }

    [Fact]
    public async Task Handle_NoOverlap_ReturnsEmptyConflictList()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }

    [Fact]
    public async Task Handle_DirectOverlap_ReturnsOneConflict()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup", StartDate = Start, EndDate = End, SourceType = CalendarEventSourceTypes.Manual, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow }]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal("Ada Lovelace", result.Value.Conflicts[0].EmployeeName);
    }

    [Fact]
    public async Task Handle_OverlapAgainstRecurringMasterOccurrence_ReturnsOneConflict()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Weekly Sync", StartDate = Start.AddDays(-7), EndDate = End.AddDays(-7),
            SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId, End, It.IsAny<CancellationToken>())).ReturnsAsync([master]);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, master.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _expander.Setup(x => x.Expand("FREQ=WEEKLY", master.StartDate, Start, End)).Returns([Start]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal("Weekly Sync", result.Value.Conflicts[0].ConflictingEventTitle);
    }

    [Fact]
    public async Task Handle_ComputesOverlapAsIntersectionOfQueryRangeAndEventRange()
    {
        var sut = BuildSut();
        // Existing event runs 09:00-10:30 against the fixture's 09:00-10:00 query range - overlap is 09:00-10:00
        var existingEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Payroll review",
            StartDate = Start, EndDate = End.AddMinutes(30),
            SourceType = CalendarEventSourceTypes.Manual, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingEvent]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var conflict = Assert.Single(result.Value!.Conflicts);
        Assert.Equal(Start, conflict.OverlapStart);
        Assert.Equal(End, conflict.OverlapEnd);
    }
    [Fact]
    public async Task Duplicate_participant_ids_are_checked_once()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId, EmployeeId], Start, End), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Availability_rows_are_appended_after_event_rows()
    {
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup", StartDate = Start, EndDate = End, SourceType = CalendarEventSourceTypes.Manual, CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow }]);
        var leave = new CalendarConflict(EmployeeId, "Ada Lovelace", Guid.Empty, "On leave", Start, End, Kind: CalendarConflictKinds.Leave);
        var availability = new Mock<IParticipantAvailabilityChecker>();
        availability.Setup(x => x.CheckAsync(TenantId, It.IsAny<Guid?>(),
                It.Is<IReadOnlyList<Employee>>(people => people.Count == 1 && people[0].Id == EmployeeId),
                Start, End, true, "Asia/Colombo", It.IsAny<CancellationToken>()))
            .ReturnsAsync([leave]);
        BuildSut();
        var sut = new CheckCalendarConflictsQueryHandler(_currentUser.Object, _events.Object, _employees.Object, _expander.Object,
            availability: availability.Object);

        var result = await sut.Handle(
            new CheckCalendarConflictsQuery([EmployeeId], Start, End, IsAllDay: true, TimeZone: "Asia/Colombo"), CancellationToken.None);

        Assert.Equal([CalendarConflictKinds.Event, CalendarConflictKinds.Leave], result.Value!.Conflicts.Select(c => c.Kind));
    }
    [Fact]
    public async Task Offset_times_are_queried_in_utc()
    {
        // Npgsql only accepts UTC DateTimeOffsets for timestamptz; the meeting form sends "...T08:45:00+05:30".
        var sut = BuildSut();
        var colomboStart = Start.ToOffset(TimeSpan.FromHours(5.5));
        var colomboEnd = End.ToOffset(TimeSpan.FromHours(5.5));
        _events.Setup(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _events.Setup(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await sut.Handle(new CheckCalendarConflictsQuery([EmployeeId], colomboStart, colomboEnd), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.GetInDateRangeForEmployeeAsync(TenantId, EmployeeId,
            It.Is<DateTimeOffset>(d => d.Offset == TimeSpan.Zero && d == Start),
            It.Is<DateTimeOffset>(d => d.Offset == TimeSpan.Zero && d == End), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(x => x.GetRecurringMastersForEmployeeAsync(TenantId, EmployeeId,
            It.Is<DateTimeOffset>(d => d.Offset == TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }
}
