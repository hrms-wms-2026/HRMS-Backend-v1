using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetCalendarEvents;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetCalendarEventsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();

    private GetCalendarEventsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId });
        _events.Setup(x => x.GetRecurringMastersForCallerAsync(TenantId, UserId, EmployeeId, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new GetCalendarEventsQueryHandler(_currentUser.Object, _employees.Object, _events.Object, _expander.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetCalendarEventsQueryHandler(_currentUser.Object, _employees.Object, _events.Object, _expander.Object);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MapsRepositoryRowsToResponse()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEvent
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, Title = "Sprint Planning",
                    StartDate = From.AddDays(2), EndDate = From.AddDays(2).AddHours(1),
                    SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.None,
                    CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Events);
        Assert.Equal("Sprint Planning", result.Value.Events[0].Title);
    }

    [Fact]
    public async Task Handle_ExpandsRecurringMaster_SkippingDetachedAndCancelledOccurrences()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Standup",
            StartDate = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 9, 1, 9, 30, 0, TimeSpan.Zero),
            SourceType = CalendarEventSourceTypes.Manual, Recurrence = CalendarRecurrences.Daily,
            RecurrenceRule = "FREQ=DAILY", CreatedById = UserId, CreatedAt = DateTimeOffset.UtcNow
        };
        _events.Setup(x => x.GetInDateRangeForCallerAsync(TenantId, UserId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _events.Setup(x => x.GetRecurringMastersForCallerAsync(TenantId, UserId, EmployeeId, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([master]);

        var detachedStart = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);
        var cancelledStart = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, master.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = master.Id, RecurrenceOriginalStart = detachedStart, IsRecurrenceCancelled = false },
                new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = master.Id, RecurrenceOriginalStart = cancelledStart, IsRecurrenceCancelled = true }
            ]);

        var occurrenceStarts = new[]
        {
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            detachedStart,
            cancelledStart
        };
        _expander.Setup(x => x.Expand("FREQ=DAILY", master.StartDate, From, To)).Returns(occurrenceStarts);

        var result = await sut.Handle(new GetCalendarEventsQuery(From, To), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Events.Count); // Sep 1 and Sep 2 only - Sep 3 detached, Sep 4 cancelled
        Assert.All(result.Value.Events, e => Assert.True(e.IsRecurringOccurrence));
        Assert.All(result.Value.Events, e => Assert.Equal(master.Id, e.RecurrenceMasterId));
    }
}
