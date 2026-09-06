using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetEligibleNominees;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetEligibleNomineesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository> _employeeRepo = new();

    private GetEligibleNomineesQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employeeRepo.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, FirstName = "Ada", LastName = "Lovelace" });
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventParticipant { EventId = EventId, EmployeeId = EmployeeId });
        return new GetEligibleNomineesQueryHandler(_currentUser.Object, _events.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task Handle_SoleParticipant_ReturnsEmptyList()
    {
        var sut = BuildSut();
        _events.Setup(x => x.GetParticipantsForEventsAsync(TenantId, new[] { EventId }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>
            {
                [EventId] = new List<CalendarEventParticipant> { new() { EventId = EventId, EmployeeId = EmployeeId } }
            });

        var result = await sut.Handle(new GetEligibleNomineesQuery(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Nominees);
    }

    [Fact]
    public async Task Handle_MultipleParticipants_ReturnsEveryoneExceptCaller()
    {
        var sut = BuildSut();
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

        var result = await sut.Handle(new GetEligibleNomineesQuery(EventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var nominee = Assert.Single(result.Value!.Nominees);
        Assert.Equal(coParticipantId, nominee.EmployeeId);
        Assert.Equal("Grace Hopper", nominee.EmployeeName);
    }

    [Fact]
    public async Task Handle_CallerNotAParticipant_ReturnsNotFound()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employeeRepo.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId });
        _events.Setup(x => x.GetTrackedParticipantAsync(TenantId, EventId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEventParticipant?)null);
        var sut = new GetEligibleNomineesQueryHandler(_currentUser.Object, _events.Object, _employeeRepo.Object);

        var result = await sut.Handle(new GetEligibleNomineesQuery(EventId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
