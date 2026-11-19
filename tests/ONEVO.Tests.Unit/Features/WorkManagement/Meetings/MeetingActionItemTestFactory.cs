using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>
/// The action-item service over mocks, for the task-create unit tests: a create with a
/// <c>sourceMeetingOccurrenceId</c> only reaches it when the source is a valid, visible occurrence.
/// </summary>
public static class MeetingActionItemTestFactory
{
    public static MeetingActionItemService Build(
        Mock<IMeetingRepository>? meetings = null,
        Mock<IMeetingAccessResolver>? access = null,
        Mock<IMeetingActionItemRepository>? links = null,
        Mock<IWorkTaskRepository>? tasks = null,
        Mock<ITaskStatusRepository>? statuses = null,
        Mock<ITaskAccessResolver>? taskAccess = null,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        meetings ??= new Mock<IMeetingRepository>();
        access ??= new Mock<IMeetingAccessResolver>();
        links ??= new Mock<IMeetingActionItemRepository>();
        tasks ??= new Mock<IWorkTaskRepository>();
        statuses ??= new Mock<ITaskStatusRepository>();
        taskAccess ??= new Mock<ITaskAccessResolver>();
        unitOfWork ??= new Mock<IUnitOfWork>();

        return new MeetingActionItemService(
            Microsoft.Extensions.Options.Options.Create(new MeetingsOptions { Create = true }),
            meetings.Object, access.Object, links.Object, tasks.Object, statuses.Object,
            taskAccess.Object, unitOfWork.Object, new Mock<IDateTimeProvider>().Object);
    }
}
