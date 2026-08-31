using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.CancelRecurringOccurrence;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
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
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private CancelRecurringOccurrenceCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new CancelRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
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
}
