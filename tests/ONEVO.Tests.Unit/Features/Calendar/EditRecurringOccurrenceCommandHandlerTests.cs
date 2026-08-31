using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.EditRecurringOccurrence;
using ONEVO.Application.Features.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class EditRecurringOccurrenceCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MasterId = Guid.NewGuid();
    private static readonly DateTimeOffset SeriesStart = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OriginalStart = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private EditRecurringOccurrenceCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<ONEVO.Application.Common.Models.Result<CalendarEventItem>>>, CancellationToken>(
                (action, ct) => action(ct));
        return new EditRecurringOccurrenceCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeMaster() => new()
    {
        Id = MasterId, TenantId = TenantId, CreatedById = UserId, Title = "Standup",
        StartDate = SeriesStart, EndDate = SeriesStart.AddMinutes(30),
        Recurrence = CalendarRecurrences.Weekly, RecurrenceRule = "FREQ=WEEKLY",
        SourceType = CalendarEventSourceTypes.Manual
    };

    private EditRecurringOccurrenceCommand MakeCommand(RecurrenceEditScope scope) => new(
        MasterId, OriginalStart, scope, "New Title", "New desc",
        OriginalStart.AddHours(1), OriginalStart.AddHours(1).AddMinutes(30), false, "UTC", "Room 2", null, "#ff0000");

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        master.CreatedById = Guid.NewGuid();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotARecurringMaster_ReturnsFailure()
    {
        var sut = BuildSut();
        var notAMaster = MakeMaster();
        notAMaster.Recurrence = CalendarRecurrences.None;
        notAMaster.RecurrenceRule = null;
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(notAMaster);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllEvents_UpdatesMasterDirectly()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.AllEvents), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", master.Title);
        _events.Verify(x => x.Update(master), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_NoExistingChild_CreatesDetachedRow()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == MasterId && e.RecurrenceOriginalStart == OriginalStart && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ThisEventOnly_ExistingChild_UpdatesIt()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var existingChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId, RecurrenceOriginalStart = OriginalStart,
            Title = "Old", StartDate = OriginalStart, EndDate = OriginalStart.AddMinutes(30)
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetTrackedChildByOriginalStartAsync(TenantId, MasterId, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisEventOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", existingChild.Title);
        _events.Verify(x => x.Update(existingChild), Times.Once);
        _events.Verify(x => x.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ThisAndFollowing_SplitsSeriesAndReparentsLaterChildren()
    {
        var sut = BuildSut();
        var master = MakeMaster();
        var earlierChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(-7), Title = "Earlier override"
        };
        var laterChild = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecurrenceParentId = MasterId,
            RecurrenceOriginalStart = OriginalStart.AddDays(7), Title = "Later override"
        };
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, MasterId, It.IsAny<CancellationToken>())).ReturnsAsync(master);
        _events.Setup(x => x.GetChildrenForMasterAsync(TenantId, MasterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlierChild, laterChild]);
        _events.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, laterChild.Id, It.IsAny<CancellationToken>())).ReturnsAsync(laterChild);

        var result = await sut.Handle(MakeCommand(RecurrenceEditScope.ThisAndFollowing), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("UNTIL=", master.RecurrenceRule);
        _events.Verify(x => x.AddAsync(It.Is<CalendarEvent>(e =>
            e.RecurrenceParentId == null && e.Recurrence == CalendarRecurrences.Weekly && e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(MasterId, earlierChild.RecurrenceParentId); // untouched - before the split point
        _events.Verify(x => x.Update(laterChild), Times.Once); // re-parented - on/after the split point
    }
}
