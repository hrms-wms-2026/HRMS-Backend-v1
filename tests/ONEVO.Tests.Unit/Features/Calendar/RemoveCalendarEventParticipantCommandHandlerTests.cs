using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RemoveCalendarEventParticipantCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid ParticipantEmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();

    private RemoveCalendarEventParticipantCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(e => e.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Owner" });
        return new RemoveCalendarEventParticipantCommandHandler(_currentUser.Object, _events.Object, _employees.Object, _notifications.Object, _unitOfWork.Object);
    }

    private static CalendarEvent MakeEvent() => new()
    {
        Id = EventId, TenantId = TenantId, Title = "Sprint planning", CreatedById = UserId,
        StartDate = DateTimeOffset.UtcNow.AddHours(1), EndDate = DateTimeOffset.UtcNow.AddHours(2)
    };

    [Fact]
    public async Task Handle_NotOrganizer_ReturnsForbidden()
    {
        var evt = MakeEvent();
        evt.CreatedById = Guid.NewGuid();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_EventNotFound_ReturnsNotFound()
    {
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ParticipantNotOnEvent_ReturnsNotFound()
    {
        var evt = MakeEvent();
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, ParticipantEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventParticipant?)null);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ValidParticipant_RemovesAndSaves()
    {
        var evt = MakeEvent();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = ParticipantEmployeeId };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, ParticipantEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _events.Verify(e => e.RemoveParticipant(participant), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RemovedParticipant_IsNotifiedBeforeTheSave()
    {
        var evt = MakeEvent();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = ParticipantEmployeeId };
        _events.Setup(e => e.GetTrackedByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        _events.Setup(e => e.GetTrackedParticipantAsync(TenantId, EventId, ParticipantEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);
        var calls = new List<string>();
        _notifications.Setup(n => n.NotifyParticipantRemovedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("notify")).Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("save")).ReturnsAsync(1);

        var result = await BuildSut().Handle(new RemoveCalendarEventParticipantCommand(EventId, ParticipantEmployeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Verify(n => n.NotifyParticipantRemovedAsync(
            TenantId, evt.Title, evt.StartDate, evt.Location, ParticipantEmployeeId, "Ada Owner", It.IsAny<CancellationToken>()), Times.Once);
        calls.Should().Equal("notify", "save");
    }
}
