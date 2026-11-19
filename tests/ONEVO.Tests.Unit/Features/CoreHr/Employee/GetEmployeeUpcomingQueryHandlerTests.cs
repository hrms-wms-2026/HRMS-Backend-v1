using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeUpcomingQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IReleaseCalendarRepository> _releases = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T08:00:00+00:00");

    public GetEmployeeUpcomingQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 30));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = _employeeUserId });
        _events.Setup(e => e.GetInDateRangeForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEvent>());
        _events.Setup(e => e.GetRecurringMastersForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEvent>());
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        _releases.Setup(r => r.ListForRecipientAsync(_tenantId, _employeeUserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UpcomingReleaseRow>());
        _events.Setup(e => e.GetParticipantsForEventsAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
    }

    private GetEmployeeUpcomingQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _events.Object, _expander.Object, _leave.Object, _releases.Object, _user.Object, _clock.Object);

    private static CalendarEvent Event(string title, string start, string end, bool isPrivate = false, bool allDay = false, string? location = null) => new()
    {
        Id = Guid.NewGuid(), Title = title, StartDate = DateTimeOffset.Parse(start), EndDate = DateTimeOffset.Parse(end),
        IsPrivate = isPrivate, IsAllDay = allDay, Location = location
    };

    private void ArrangeEvents(params CalendarEvent[] events) =>
        _events.Setup(e => e.GetInDateRangeForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Handle_Returns400_ForAnOutOfRangeWindow(int days)
    {
        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, days), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ExcludesPrivateCalendarEvents_AndKeepsTheRest()
    {
        ArrangeEvents(
            Event("Team meeting", "2026-10-03T09:00:00+00:00", "2026-10-03T10:00:00+00:00", location: "Room 4"),
            Event("Dentist", "2026-10-02T09:00:00+00:00", "2026-10-02T10:00:00+00:00", isPrivate: true));

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Kind.Should().Be("calendar");
        item.Title.Should().Be("Team meeting");
        item.Detail.Should().Be("Room 4");
    }

    [Fact]
    public async Task Handle_ExpandsRecurringSeries_SkipsOverriddenOccurrencesAndPrivateSeries()
    {
        var master = Event("Standup", "2026-09-01T09:00:00+00:00", "2026-09-01T09:15:00+00:00");
        master.RecurrenceRule = "FREQ=DAILY";
        var privateMaster = Event("Therapy", "2026-09-01T12:00:00+00:00", "2026-09-01T13:00:00+00:00", isPrivate: true);
        privateMaster.RecurrenceRule = "FREQ=WEEKLY";
        _events.Setup(e => e.GetRecurringMastersForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { master, privateMaster });

        var overridden = DateTimeOffset.Parse("2026-10-02T09:00:00+00:00");
        _events.Setup(e => e.GetChildrenForMasterAsync(_tenantId, master.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CalendarEvent { Id = Guid.NewGuid(), RecurrenceOriginalStart = overridden } });
        _expander.Setup(x => x.Expand("FREQ=DAILY", master.StartDate, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
            .Returns(new[] { DateTimeOffset.Parse("2026-10-01T09:00:00+00:00"), overridden, DateTimeOffset.Parse("2026-10-03T09:00:00+00:00") });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Select(i => i.Start).Should().Equal(
            DateTimeOffset.Parse("2026-10-01T09:00:00+00:00"), DateTimeOffset.Parse("2026-10-03T09:00:00+00:00"));
        result.Value.Items.Should().OnlyContain(i => i.Title == "Standup" && i.End == i.Start + TimeSpan.FromMinutes(15));
        _events.Verify(e => e.GetChildrenForMasterAsync(_tenantId, privateMaster.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AddsApprovedLeaveAndReleaseReminders()
    {
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId,
                It.Is<LeaveRequestListFilter>(f => f.Status == "approved" && f.FromDate == new DateOnly(2026, 9, 30) && f.ToDate == new DateOnly(2026, 10, 14)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LeaveRequestListRow(new LeaveRequest
                {
                    Id = Guid.NewGuid(), Status = "approved",
                    StartAt = DateTimeOffset.Parse("2026-10-06T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-10-08T23:59:59+00:00")
                }, "Annual leave", "AL")
            });
        _releases.Setup(r => r.ListForRecipientAsync(_tenantId, _employeeUserId, new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UpcomingReleaseRow(Guid.NewGuid(), new DateOnly(2026, 10, 1), "project_release", null, "v2.0", "Website") });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var items = result.Value!.Items;
        items.Select(i => i.Kind).Should().Equal("release", "leave");
        items[0].Title.Should().Be("v2.0");
        items[0].Detail.Should().Be("Website");
        items[0].IsAllDay.Should().BeTrue();
        items[1].Title.Should().Be("Annual leave");
        items[1].Detail.Should().Be("Approved");
        items[1].End.Should().Be(DateTimeOffset.Parse("2026-10-08T23:59:59+00:00"));
    }

    [Fact]
    public async Task Handle_SortsEarliestFirst_AndReturnsAtMostTenItems()
    {
        ArrangeEvents(Enumerable.Range(1, 12)
            .Select(i => Event($"Event {i}", $"2026-10-{i:00}T09:00:00+00:00", $"2026-10-{i:00}T10:00:00+00:00"))
            .Reverse()
            .ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 30), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(10);
        result.Value.Items.Select(i => i.Title).First().Should().Be("Event 1");
        result.Value.Items.Select(i => i.Start).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Handle_CarriesCalendarDetailsAndParticipantNames_ForThePopup()
    {
        var meeting = Event("Demo", "2026-10-02T09:00:00+00:00", "2026-10-02T10:00:00+00:00", location: "Teams");
        meeting.Description = "Sprint demo";
        meeting.MeetingLink = "https://teams.example/meet";
        meeting.OrganizerName = "Grace Hopper";
        meeting.Timezone = "Asia/Colombo";
        ArrangeEvents(meeting);
        var aliceId = Guid.NewGuid();
        _events.Setup(e => e.GetParticipantsForEventsAsync(_tenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { meeting.Id })), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [meeting.Id] = new[] { new CalendarEventParticipant { EventId = meeting.Id, EmployeeId = aliceId } }
            });
        _employees.Setup(e => e.GetByIdAsync(_tenantId, aliceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = aliceId, TenantId = _tenantId, FirstName = "Alice", LastName = "Smith" });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Description.Should().Be("Sprint demo");
        item.MeetingLink.Should().Be("https://teams.example/meet");
        item.OrganizerName.Should().Be("Grace Hopper");
        item.Timezone.Should().Be("Asia/Colombo");
        item.Participants.Should().Equal("Alice Smith");
    }

    [Fact]
    public async Task Handle_CarriesLeaveHours_AndSkipsParticipantLookupWithoutEvents()
    {
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LeaveRequestListRow(new LeaveRequest
                {
                    Id = Guid.NewGuid(), Status = "approved", TotalHours = 16,
                    StartAt = DateTimeOffset.Parse("2026-10-06T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-10-07T23:59:59+00:00")
                }, "Annual leave", "AL")
            });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Hours.Should().Be(16);
        item.Participants.Should().BeNull();
        _events.Verify(e => e.GetParticipantsForEventsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheEarliestLimitItems_ButCountsTheWholeWindow()
    {
        ArrangeEvents(Enumerable.Range(1, 5)
            .Select(i => Event($"Event {i}", $"2026-10-{i:00}T09:00:00+00:00", $"2026-10-{i:00}T10:00:00+00:00"))
            .ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 14, 3), CancellationToken.None);

        result.Value!.Items.Select(i => i.Title).Should().Equal("Event 1", "Event 2", "Event 3");
        result.Value.Total.Should().Be(5);
        _events.Verify(e => e.GetParticipantsForEventsAsync(_tenantId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Handle_RejectsAnOutOfRangeLimit(int limit)
    {
        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 14, limit), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsTheWindowDates()
    {
        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 7), CancellationToken.None);

        result.Value!.From.Should().Be(new DateOnly(2026, 9, 30));
        result.Value.To.Should().Be(new DateOnly(2026, 10, 7));
    }
}
