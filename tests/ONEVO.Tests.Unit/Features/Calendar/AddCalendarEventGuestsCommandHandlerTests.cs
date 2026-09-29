using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.AddCalendarEventGuests;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class AddCalendarEventGuestsCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private AddCalendarEventGuestsCommandHandler BuildSut(IReadOnlyList<CalendarEventGuest>? existingGuests = null, CalendarEvent? evt = null)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventGuestsResult>>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventGuestsResult>>>, CancellationToken>((op, ct) => op(ct));
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt ?? MakeEvent());
        _events.Setup(e => e.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [EventId] = existingGuests ?? [] });
        return new AddCalendarEventGuestsCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Design review", CreatedById = UserId, Location = "Room 4",
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2),
        MeetingLink = "https://us05web.zoom.us/j/123"
    };

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = MakeEvent();
        evt.CreatedById = Guid.NewGuid();

        var result = await BuildSut(evt: evt).Handle(new AddCalendarEventGuestsCommand(EventId, ["g@x.co"]), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _events.Verify(e => e.AddGuestsAsync(It.IsAny<IReadOnlyList<CalendarEventGuest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(new AddCalendarEventGuestsCommand(EventId, ["g@x.co"]), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_InvalidEmail_Returns400NamingTheAddress_AndSavesNothing()
    {
        var result = await BuildSut().Handle(new AddCalendarEventGuestsCommand(EventId, ["ok@x.co", "nope"]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("nope");
        _events.Verify(e => e.AddGuestsAsync(It.IsAny<IReadOnlyList<CalendarEventGuest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewEmails_AreStoredNormalized_AndInvitedWithTheMeetingLink()
    {
        var evt = MakeEvent();

        var result = await BuildSut(evt: evt).Handle(
            new AddCalendarEventGuestsCommand(EventId, ["  Guest@Example.com ", "guest@example.com", "other@example.com"]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Guests.Select(g => g.Email).Should().Equal("guest@example.com", "other@example.com");
        _events.Verify(e => e.AddGuestsAsync(
            It.Is<IReadOnlyList<CalendarEventGuest>>(list => list.Count == 2 && list.All(g => g.TenantId == TenantId && g.EventId == EventId)),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.NotifyGuestsAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location,
            It.Is<IReadOnlyList<string>>(emails => emails.SequenceEqual(new[] { "guest@example.com", "other@example.com" })),
            "Ada Owner", evt.MeetingLink, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyInvitedGuest_IsNotInvitedAgain()
    {
        var existing = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "guest@example.com" };

        var result = await BuildSut([existing]).Handle(new AddCalendarEventGuestsCommand(EventId, ["GUEST@example.com"]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Guests.Should().ContainSingle(g => g.Email == "guest@example.com");
        _events.Verify(e => e.AddGuestsAsync(It.IsAny<IReadOnlyList<CalendarEventGuest>>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.NotifyGuestsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoreThanTwentyGuestsInTotal_Returns400()
    {
        var existing = Enumerable.Range(0, 19)
            .Select(i => new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = $"g{i}@x.co" })
            .ToList();

        var result = await BuildSut(existing).Handle(new AddCalendarEventGuestsCommand(EventId, ["a@x.co", "b@x.co"]), CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("20");
        _events.Verify(e => e.AddGuestsAsync(It.IsAny<IReadOnlyList<CalendarEventGuest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
