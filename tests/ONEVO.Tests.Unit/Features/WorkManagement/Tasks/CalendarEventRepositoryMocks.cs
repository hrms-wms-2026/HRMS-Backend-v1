using Moq;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

/// <summary>A calendar-event repo mock that reports no event membership - the neutral
/// default for task command/query tests that don't exercise the event-window guards.</summary>
internal static class CalendarEventRepositoryMocks
{
    public static Mock<ICalendarEventRepository> Empty()
    {
        var mock = new Mock<ICalendarEventRepository>();
        mock.Setup(x => x.ListActiveEventWindowsForObjectiveAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveEventWindow>());
        mock.Setup(x => x.ListActiveEventWindowsForTaskAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveEventWindow>());
        mock.Setup(x => x.ListActiveTaskLinksForTasksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActiveCalendarEventTaskLink>());
        return mock;
    }
}
