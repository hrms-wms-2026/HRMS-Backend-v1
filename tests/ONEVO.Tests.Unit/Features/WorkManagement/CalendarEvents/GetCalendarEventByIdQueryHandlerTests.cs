using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class GetCalendarEventByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsDetailWithDescriptionAndOwnerName()
    {
        var eventId = Guid.NewGuid();
        var h = new Harness();
        h.WithEvent(new CalendarEvent
        {
            Id = eventId, TenantId = TenantId, ProjectId = ProjectId, Name = "Release 1", Color = "#2563EB",
            Status = CalendarEventStatuses.Active, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
            Description = "Covers the Q3 release scope.", CreatedById = EmployeeId, CreatedAt = DateTimeOffset.UtcNow
        });
        h.WithDisplayName(EmployeeId, "Priya Shankar");

        var result = await h.Handle(new GetCalendarEventByIdQuery(eventId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Covers the Q3 release scope.", result.Value!.Description);
        Assert.Equal("Priya Shankar", result.Value!.CreatedByName);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        var h = new Harness();

        var result = await h.Handle(new GetCalendarEventByIdQuery(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private sealed class Harness
    {
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<ICallerIdentityResolver> _identity = new();
        private readonly Mock<ICalendarEventRepository> _events = new();
        private readonly Mock<IProjectMemberRepository> _members = new();

        public Harness()
        {
            _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            _currentUser.SetupGet(x => x.UserId).Returns(UserId);
            _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmployeeId);
            _events.Setup(x => x.GetByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CalendarEvent?)null);
            _events.Setup(x => x.ListMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventObjective>());
            _events.Setup(x => x.ListTaskMembershipsForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventTask>());
            _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string>());
        }

        public void WithEvent(CalendarEvent calendarEvent)
            => _events.Setup(x => x.GetByIdForTenantAsync(TenantId, calendarEvent.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(calendarEvent);

        public void WithDisplayName(Guid employeeId, string name)
            => _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(employeeId)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string> { [employeeId] = name });

        public Task<ONEVO.Application.Common.Models.Result<CalendarEventDetailResponse>> Handle(GetCalendarEventByIdQuery query)
        {
            var handler = new GetCalendarEventByIdQueryHandler(
                _currentUser.Object, _identity.Object, _events.Object, _members.Object);
            return handler.Handle(query, CancellationToken.None);
        }
    }
}
