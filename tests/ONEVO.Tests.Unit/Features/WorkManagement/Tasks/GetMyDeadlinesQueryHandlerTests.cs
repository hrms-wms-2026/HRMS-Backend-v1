using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyDeadlinesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsOwnedObjectivesAndAssignedTasksInRange()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetOwnedByEmployeeIdWithinRangeAsync(TenantId, EmployeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>
            {
                new()
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, Title = "Milestone A",
                    EndDate = new DateOnly(2026, 8, 15), OwnerId = EmployeeId, CreatedAt = DateTimeOffset.UtcNow
                }
            });

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetAssignedToEmployeeWithinRangeAsync(TenantId, EmployeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new()
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, Title = "Task A", ShortId = "T-1",
                    DueDate = new DateOnly(2026, 8, 20), CreatedAt = DateTimeOffset.UtcNow
                }
            });

        var handler = new GetMyDeadlinesQueryHandler(currentUser.Object, identity.Object, objectives.Object, tasks.Object);
        var result = await handler.Handle(new GetMyDeadlinesQuery(from, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ObjectiveDeadlines);
        Assert.Single(result.Value!.TaskDeadlines);
    }
}
