using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.RespondToCalendarEvent;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
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

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private RespondToCalendarEventCommandHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employeeRepo.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId });
        return new RespondToCalendarEventCommandHandler(_currentUser.Object, _events.Object, _employeeRepo.Object, _unitOfWork.Object);
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
}
