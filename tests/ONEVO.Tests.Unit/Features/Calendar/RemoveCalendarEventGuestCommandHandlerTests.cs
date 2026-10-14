using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventGuest;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveCalendarEventGuestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();

    private RemoveCalendarEventGuestCommandHandler BuildSut(Guid? creatorId = null, bool eventExists = true)
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventExists ? new CalendarEvent { Id = EventId, TenantId = TenantId, Title = "T", CreatedById = creatorId ?? UserId } : null);
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        return new RemoveCalendarEventGuestCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sprint planning", CreatedById = UserId,
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2)
    };

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var result = await BuildSut(creatorId: Guid.NewGuid()).Handle(new RemoveCalendarEventGuestCommand(EventId, "g@x.co"), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        var result = await BuildSut(eventExists: false).Handle(new RemoveCalendarEventGuestCommand(EventId, "g@x.co"), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_InvalidEmail_Returns400()
    {
        var result = await BuildSut().Handle(new RemoveCalendarEventGuestCommand(EventId, "nope"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_GuestNotInvited_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(e => e.GetTrackedGuestAsync(TenantId, EventId, "g@x.co", It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEventGuest?)null);

        var result = await sut.Handle(new RemoveCalendarEventGuestCommand(EventId, "g@x.co"), CancellationToken.None);

        result.StatusCode.Should().Be(404);
        _events.Verify(e => e.RemoveGuest(It.IsAny<CalendarEventGuest>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvitedGuest_IsRemovedByNormalizedEmail()
    {
        var sut = BuildSut();
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "g@x.co" };
        _events.Setup(e => e.GetTrackedGuestAsync(TenantId, EventId, "g@x.co", It.IsAny<CancellationToken>())).ReturnsAsync(guest);

        var result = await sut.Handle(new RemoveCalendarEventGuestCommand(EventId, "  G@X.co "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _events.Verify(e => e.RemoveGuest(guest), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RemovedGuest_IsEmailedBeforeTheSave()
    {
        var sut = BuildSut();
        var evt = MakeEvent();
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, Email = "vendor@example.com" };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedGuestAsync(TenantId, EventId, "vendor@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(guest);
        var calls = new List<string>();
        _notifications.Setup(n => n.NotifyGuestRemovedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        var result = await sut.Handle(new RemoveCalendarEventGuestCommand(EventId, "Vendor@Example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Verify(n => n.NotifyGuestRemovedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location, "vendor@example.com", "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        calls.Should().Equal("notify", "save");
    }
}
