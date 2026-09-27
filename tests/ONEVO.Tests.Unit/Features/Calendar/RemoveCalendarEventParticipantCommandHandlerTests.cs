using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RemoveCalendarEventParticipant;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
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

    private RemoveCalendarEventParticipantCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new RemoveCalendarEventParticipantCommandHandler(_currentUser.Object, _events.Object, _unitOfWork.Object);
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
}
