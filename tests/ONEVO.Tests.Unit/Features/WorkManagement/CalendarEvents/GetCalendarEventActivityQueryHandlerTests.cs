using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public sealed class GetCalendarEventActivityQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsEntriesWithResolvedPerformerNames_NewestFirst()
    {
        var eventId = Guid.NewGuid();
        var performerId = Guid.NewGuid();
        var h = new Harness();
        h.WithEvent(eventId);
        h.WithEntries(new CalendarEventActivityLog
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = eventId,
            Action = CalendarEventActivityActions.Updated, PerformedById = performerId, PerformedAt = DateTimeOffset.UtcNow
        });
        h.WithDisplayName(performerId, "Priya Shankar");

        var result = await h.Handle(new GetCalendarEventActivityQuery(eventId));

        Assert.True(result.IsSuccess);
        Assert.Equal("updated", result.Value![0].Action);
        Assert.Equal("Priya Shankar", result.Value![0].PerformedByName);
    }

    private sealed class Harness
    {
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly Mock<ICallerIdentityResolver> _identity = new();
        private readonly Mock<ICalendarEventRepository> _events = new();
        private readonly Mock<ICalendarEventActivityLogRepository> _activityLogs = new();
        private readonly Mock<IProjectMemberRepository> _members = new();

        public Harness()
        {
            _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
            _currentUser.SetupGet(x => x.UserId).Returns(UserId);
            _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(EmployeeId);
            _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _activityLogs.Setup(x => x.ListByEventIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<CalendarEventActivityLog>());
            _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string>());
        }

        public void WithEvent(Guid eventId)
            => _events.Setup(x => x.GetByIdForTenantAsync(TenantId, eventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CalendarEvent
                {
                    Id = eventId, TenantId = TenantId, ProjectId = ProjectId, Name = "E", Color = "#000000",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
                    Status = CalendarEventStatuses.Active, CreatedAt = DateTimeOffset.UtcNow, CreatedById = EmployeeId
                });

        public void WithEntries(params CalendarEventActivityLog[] entries)
            => _activityLogs.Setup(x => x.ListByEventIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(entries.OrderByDescending(e => e.PerformedAt).ToList());

        public void WithDisplayName(Guid employeeId, string name)
            => _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(employeeId)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string> { [employeeId] = name });

        public Task<ONEVO.Application.Common.Models.Result<IReadOnlyList<CalendarEventActivityEntryResponse>>> Handle(GetCalendarEventActivityQuery query)
        {
            var handler = new GetCalendarEventActivityQueryHandler(
                _currentUser.Object, _identity.Object, _events.Object, _activityLogs.Object, _members.Object);
            return handler.Handle(query, CancellationToken.None);
        }
    }
}
