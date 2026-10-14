using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CancelRecurringOccurrenceCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly DateTimeOffset OriginalStart = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CancelRecurringOccurrenceCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>());
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>>());
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result>>, CancellationToken>((op, ct) => op(ct));
        return new CancelRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeMaster() => new()
    {
        Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY", SourceType = CalendarEventSourceTypes.Manual
    };

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.CreatedById = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoExistingChild_CreatesCancellationMarker()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == MasterId && e.RecurrenceOriginalStart == OriginalStart && e.IsRecurrenceCancelled),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingChild_MarksItCancelled_Idempotently()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var existingChild = new CalendarEvent { Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingChild);

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existingChild.IsRecurrenceCancelled);
        _events.Verify(x => x.Update(existingChild), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MasterWithParticipants_NotifiesThem()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var participantId = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(MasterId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [MasterId] = [new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = participantId, ResponseStatus = CalendarEventParticipantStatuses.Accepted }]
            });

        var result = await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, OriginalStart), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _notifications.Verify(x => x.NotifyEventCancelledAsync(
            TenantId, "Standup", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Count == 1 && r[0].EmployeeId == participantId), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotifiesParticipantsAndGuestsBeforeSaving_KeyedOnTheOccurrence()
    {
        var sut = BuildSut();
        var master = new CalendarEvent
        {
            Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup", Location = "Room 4",
            StartDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), EndDate = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero),
            Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY"
        };
        var originalStart = master.StartDate.AddDays(7);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, originalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, EmployeeId = Guid.NewGuid() };
        var guest = new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = MasterId, Email = "vendor@example.com" };
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>> { [MasterId] = [participant] });
        _events.Setup(x => x.GetGuestsForEventsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventGuest>> { [MasterId] = [guest] });
        var calls = new List<string>();
        _notifications.Setup(x => x.NotifyEventCancelledAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<CalendarParticipantRecipient>>(), It.IsAny<IReadOnlyList<CalendarGuestRecipient>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        await sut.Handle(new CancelRecurringOccurrenceCommand(MasterId, originalStart), CancellationToken.None);

        Assert.Equal(new[] { "notify", "save" }, calls);
        _notifications.Verify(x => x.NotifyEventCancelledAsync(
            TenantId, "Standup", originalStart, "Room 4",
            It.Is<IReadOnlyList<CalendarParticipantRecipient>>(r => r.Single().EmailKey == $"cancel:{MasterId}:{originalStart.UtcTicks}:{participant.Id}"),
            It.Is<IReadOnlyList<CalendarGuestRecipient>>(g => g.Single().EmailKey == $"cancel:{MasterId}:{originalStart.UtcTicks}:{guest.Id}"),
            "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
    }
}
