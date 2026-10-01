using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class WorkLeadershipServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Me = Guid.NewGuid(), Le = Guid.NewGuid(), Project = Guid.NewGuid();

    private static WorkLeadershipService Build(
        Mock<IObjectiveRepository> objectives,
        Mock<IProjectMemberRepository>? members = null,
        Mock<ITaskCreationRequestRepository>? taskCreation = null,
        Mock<ITaskEditRequestRepository>? taskEdit = null,
        Mock<IObjectiveChangeRequestRepository>? objectiveChange = null,
        Mock<ITaskStatusChangeRequestRepository>? statusChangeRequests = null,
        Mock<ITaskStatusChangeAccessService>? statusChangeAccess = null)
    {
        members ??= new Mock<IProjectMemberRepository>(MockBehavior.Strict);
        taskCreation ??= Default(new Mock<ITaskCreationRequestRepository>(), m =>
            m.Setup(x => x.HasPendingForOwnerEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false));
        taskEdit ??= Default(new Mock<ITaskEditRequestRepository>(), m =>
            m.Setup(x => x.HasPendingForOwnerEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false));
        objectiveChange ??= Default(new Mock<IObjectiveChangeRequestRepository>(), m =>
            m.Setup(x => x.HasPendingForApproverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false));
        statusChangeRequests ??= Default(new Mock<ITaskStatusChangeRequestRepository>(), m =>
            m.Setup(x => x.ListAllPendingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TaskStatusChangeRequest>()));
        statusChangeAccess ??= new Mock<ITaskStatusChangeAccessService>(MockBehavior.Strict);

        return new WorkLeadershipService(
            objectives.Object, members.Object, taskCreation.Object, taskEdit.Object,
            objectiveChange.Object, statusChangeRequests.Object, statusChangeAccess.Object,
            NullLogger<WorkLeadershipService>.Instance);
    }

    private static Mock<T> Default<T>(Mock<T> mock, Action<Mock<T>> setup) where T : class
    {
        setup(mock);
        return mock;
    }

    [Fact]
    public async Task No_owned_modules_short_circuits_without_loading_trees()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid)>());
        var sut = Build(objectives);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Same(LedWorkScope.Empty, scope);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Builds_the_scope_from_one_tree_read_and_one_membership_read()
    {
        Guid root = Guid.NewGuid(), module = Guid.NewGuid();
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.ListActiveOwnedIdsAsync(Tenant, Me, Le, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (module, Project) });
        objectives.Setup(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LedObjectiveRow(root, Project, null, "Root", true, new DateOnly(2026, 12, 1)),
                new LedObjectiveRow(module, Project, root, "Payments", false, new DateOnly(2026, 11, 1)),
            });
        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveMembershipObjectiveIdsAsync(Tenant, Me, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { module });
        var sut = Build(objectives, members);

        var scope = await sut.ResolveLedScopeAsync(Tenant, Me, Le);

        Assert.Equal(module, Assert.Single(scope.HeadModules).ObjectiveId);
        objectives.Verify(x => x.ListActiveTreeForProjectsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeadsAnyWorkAsync_delegates_to_the_repository_probe()
    {
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.AnyActiveOwnedAsync(Tenant, Me, Le, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = Build(objectives);

        Assert.True(await sut.LeadsAnyWorkAsync(Tenant, Me, Le));
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_false_when_all_four_predicates_are_empty()
    {
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict));

        Assert.False(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_true_from_task_creation_short_circuits_the_rest()
    {
        var taskCreation = new Mock<ITaskCreationRequestRepository>();
        taskCreation.Setup(x => x.HasPendingForOwnerEmployeeIdAsync(Tenant, Me, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var taskEdit = new Mock<ITaskEditRequestRepository>(MockBehavior.Strict);
        var objectiveChange = new Mock<IObjectiveChangeRequestRepository>(MockBehavior.Strict);
        var statusRequests = new Mock<ITaskStatusChangeRequestRepository>(MockBehavior.Strict);
        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict), taskCreation: taskCreation, taskEdit: taskEdit,
            objectiveChange: objectiveChange, statusChangeRequests: statusRequests);

        Assert.True(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
        taskEdit.VerifyNoOtherCalls();
        objectiveChange.VerifyNoOtherCalls();
        statusRequests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HasPendingWorkApprovalsAsync_checks_status_template_change_by_distinct_project_not_per_row()
    {
        var approverProject = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        var statusRequests = new Mock<ITaskStatusChangeRequestRepository>();
        statusRequests.Setup(x => x.ListAllPendingAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new TaskStatusChangeRequest { Id = Guid.NewGuid(), TenantId = Tenant, ProjectId = otherProject, RequestedByEmployeeId = Guid.NewGuid() },
            new TaskStatusChangeRequest { Id = Guid.NewGuid(), TenantId = Tenant, ProjectId = otherProject, RequestedByEmployeeId = Guid.NewGuid() },
            new TaskStatusChangeRequest { Id = Guid.NewGuid(), TenantId = Tenant, ProjectId = approverProject, RequestedByEmployeeId = Guid.NewGuid() },
        });
        var access = new Mock<ITaskStatusChangeAccessService>();
        access.Setup(x => x.ResolveAsync(Tenant, otherProject, Me, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatusChangeAccess(new Objective { Id = Guid.NewGuid(), TenantId = Tenant, Title = "Root" }, false, true));
        access.Setup(x => x.ResolveAsync(Tenant, approverProject, Me, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatusChangeAccess(new Objective { Id = Guid.NewGuid(), TenantId = Tenant, Title = "Root" }, true, false));

        var sut = Build(new Mock<IObjectiveRepository>(MockBehavior.Strict), statusChangeRequests: statusRequests, statusChangeAccess: access);

        Assert.True(await sut.HasPendingWorkApprovalsAsync(Tenant, Me, Le));
        access.Verify(x => x.ResolveAsync(Tenant, otherProject, Me, It.IsAny<CancellationToken>()), Times.Once);
    }
}
