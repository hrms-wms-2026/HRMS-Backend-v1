using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class RespondToCalendarEventCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid OrganizerUserId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICalendarNotificationSender> _notifications = new();

    private RespondToCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employeeRepo.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Lovelace" });
        _events.Setup(x => x.GetByIdForTenantAsync(TenantId, EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent
            {
                Id = EventId, TenantId = TenantId, Title = "Payroll review", CreatedById = OrganizerUserId,
                SourceType = CalendarEventSourceTypes.Manual, CreatedAt = DateTimeOffset.UtcNow
            });
        return new RespondToCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employeeRepo.Object, _unitOfWork.Object, _notifications.Object);
    }

    [Fact]
    public async Task Handle_CallerNotAParticipant_ReturnsNotFound()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventParticipant?)null);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Accepted"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidParticipant_UpdatesResponseStatus()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Accepted"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.Accepted, participant.ResponseStatus);
    }

    [Fact]
    public async Task Handle_ReAnsweringAlreadyDecidedInvitation_Succeeds()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Accepted };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Rejected"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.Rejected, participant.ResponseStatus);
    }

    [Fact]
    public async Task Handle_InvalidResponseStatus_ReturnsFailure()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "Maybe"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ResolutionRequested_RequiresReasonAndSetsStatus()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var result = await sut.Handle(new RespondToCalendarEventCommand(EventId, "ResolutionRequested", Reason: null), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);

        var resultWithReason = await sut.Handle(new RespondToCalendarEventCommand(EventId, "ResolutionRequested", Reason: "Need to check with my team"), CancellationToken.None);
        Assert.True(resultWithReason.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.ResolutionRequested, participant.ResponseStatus);
        Assert.Equal("Need to check with my team", participant.ResponseReason);
        _notifications.Verify(n => n.NotifyResolutionRequestedAsync(
            TenantId, OrganizerUserId, "Payroll review", "Ada Lovelace", "Need to check with my team", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplacementNominated_RequiresNomineeAndValidatesEligibility()
    {
        var sut = BuildSut();
        var participant = new CalendarEventParticipant { Id = Guid.NewGuid(), TenantId = TenantId, EventId = EventId, EmployeeId = EmployeeId, ResponseStatus = CalendarEventParticipantStatuses.Pending };
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(participant);

        var someOutsiderId = Guid.NewGuid();
        var coParticipantId = Guid.NewGuid();
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, new[] { EventId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = new List<CalendarEventParticipant>
                {
                    new() { EventId = EventId, EmployeeId = EmployeeId },
                    new() { EventId = EventId, EmployeeId = coParticipantId }
                }
            });
        _employeeRepo.Setup(x => x.GetByIdAsync(TenantId, coParticipantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = coParticipantId, TenantId = TenantId, FirstName = "Grace", LastName = "Hopper" });

        var invalidResult = await sut.Handle(new RespondToCalendarEventCommand(EventId, "ReplacementNominated", NomineeEmployeeId: someOutsiderId), CancellationToken.None);
        Assert.False(invalidResult.IsSuccess);

        var validResult = await sut.Handle(new RespondToCalendarEventCommand(EventId, "ReplacementNominated", NomineeEmployeeId: coParticipantId), CancellationToken.None);
        Assert.True(validResult.IsSuccess);
        Assert.Equal(CalendarEventParticipantStatuses.ReplacementNominated, participant.ResponseStatus);
        _notifications.Verify(n => n.NotifyReplacementNominatedAsync(
            TenantId, OrganizerUserId, "Payroll review", "Ada Lovelace", "Grace Hopper", It.IsAny<CancellationToken>()), Times.Once);
    }
}
