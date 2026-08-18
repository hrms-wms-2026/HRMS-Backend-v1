using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetObjectiveTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsAllTasksForObjective()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
            });

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>());

        var handler = new GetObjectiveTasksQueryHandler(currentUser.Object, tasks.Object, assignments.Object);
        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task Handle_PopulatesAssigneeEmployeeIds_FromBulkAssignmentLookup()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var taskWithAssignee = Guid.NewGuid();
        var taskWithoutAssignee = Guid.NewGuid();
        var assigneeEmployeeId = Guid.NewGuid();

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = taskWithAssignee, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = taskWithoutAssignee, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
            });

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>
            {
                new() { Id = Guid.NewGuid(), TaskId = taskWithAssignee, EmployeeId = assigneeEmployeeId, UserId = Guid.NewGuid(), AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetObjectiveTasksQueryHandler(currentUser.Object, tasks.Object, assignments.Object);
        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var assigned = result.Value!.Single(t => t.Id == taskWithAssignee);
        var unassigned = result.Value!.Single(t => t.Id == taskWithoutAssignee);
        Assert.Equal([assigneeEmployeeId], assigned.AssigneeEmployeeIds);
        Assert.Empty(unassigned.AssigneeEmployeeIds!);
    }
}
