using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>
/// WM-02, application layer: two CompleteSprintCommandHandler instances - each wired to its own
/// EfSprintRepository/ApplicationDbContext sharing one InMemory database, exactly as two
/// concurrent HTTP requests each get their own per-request DbContext - racing Handle() for the
/// SAME sprint via Task.WhenAll. Before the WM-02 fix this double-completed the sprint
/// (last-write-wins, no exception); after it, exactly one request succeeds and the other gets a
/// clean 409 instead of a 500 or a silent double-apply.
/// </summary>
public sealed class SprintConcurrencyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Complete_TwoConcurrentRequests_ExactlyOneSucceedsAndOneGetsConflict()
    {
        var dbName = Guid.NewGuid().ToString();
        var sprintId = Guid.NewGuid();

        await using (var seedDb = BuildDb(dbName))
        {
            seedDb.Sprints.Add(new Sprint
            {
                Id = sprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "S1",
                Status = SprintStatuses.Active, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        await using var dbA = BuildDb(dbName);
        await using var dbB = BuildDb(dbName);

        var handlerA = BuildHandler(new EfSprintRepository(dbA));
        var handlerB = BuildHandler(new EfSprintRepository(dbB));

        var command = new CompleteSprintCommand(sprintId, "backlog", null);

        var taskA = handlerA.Handle(command, CancellationToken.None);
        var taskB = handlerB.Handle(command, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var loser = Assert.Single(results, r => !r.IsSuccess);
        Assert.Equal(409, loser.StatusCode);
    }

    private static CompleteSprintCommandHandler BuildHandler(ISprintRepository sprints)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMember>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Name = "Proj", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetBySprintIdAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>());

        var statuses = new Mock<ITaskStatusRepository>();
        var assignment = new Mock<ISprintTaskAssignmentService>();
        var logs = new Mock<ISprintActivityLogRepository>();
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);
        var notifications = new Mock<INotificationDispatcher>();

        var hierarchy = new Mock<IWorkHierarchyService>();
        var root = new ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true, OwnerId = CallerEmployeeId, Title = "Root"
        };
        hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProjectModuleTree(new List<ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective> { root }));

        var approvals = new Mock<IWorkApprovalEngine>();
        approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

        var workNotifications = new Mock<IWorkNotificationEngine>();
        workNotifications.Setup(x => x.NotifyAsync(It.IsAny<WorkNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintActionOutcome>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintActionOutcome>>> op, CancellationToken ct) => op(ct));

        var writes = new SprintWriteService(projects.Object, sprints, tasks.Object, statuses.Object, assignment.Object,
            logs.Object, new SprintAudienceResolver(tasks.Object, members.Object), membership.Object, notifications.Object);
        var submitter = new SprintActionSubmitter(hierarchy.Object, members.Object, identity.Object, unitOfWork.Object,
            approvals.Object, workNotifications.Object, sprints);

        return new CompleteSprintCommandHandler(currentUser.Object, identity.Object, sprints, members.Object, writes, submitter);
    }

    private static ApplicationDbContext BuildDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object),
            new SoftDeleteInterceptor(dateTimeProvider.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
