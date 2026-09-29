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
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>
/// Real SprintWriteService + SprintActionSubmitter over mocks for the Sprint command handler tests.
/// The approval engine is Direct by default; the project tree is a single root Module owned by
/// RootOwner (add Modules with <see cref="Modules"/>). Replace any mock before building.
/// </summary>
public sealed class SprintTestWiring
{
    public static readonly Guid RequestId = Guid.NewGuid();

    public SprintTestWiring(Guid tenantId, Guid projectId)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Root = new Objective { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, IsDefault = true, IsActive = true, OwnerId = RootOwner, Title = "Root" };
        Modules.Add(Root);

        Hierarchy.Setup(x => x.LoadTreeAsync(tenantId, projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProjectModuleTree(Modules));
        Members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, projectId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        Members.Setup(x => x.ListActiveForObjectiveAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMember>());
        EngineDirect();
        WorkNotifications.Setup(x => x.NotifyAsync(It.IsAny<WorkNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkNotificationEvent, CancellationToken>((e, _) => Notified.Add(e)).Returns(Task.CompletedTask);
        UnitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintActionOutcome>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintActionOutcome>>> op, CancellationToken ct) => op(ct));
        UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid RootOwner { get; } = Guid.NewGuid();
    public Objective Root { get; }
    public List<Objective> Modules { get; } = new();
    public List<WorkAction> Submitted { get; } = new();
    public List<WorkNotificationEvent> Notified { get; } = new();

    public Mock<IProjectRepository> Projects { get; set; } = new();
    public Mock<ISprintRepository> Sprints { get; set; } = new();
    public Mock<IWorkTaskRepository> Tasks { get; set; } = new();
    public Mock<ITaskStatusRepository> Statuses { get; set; } = new();
    public Mock<ISprintTaskAssignmentService> Assignment { get; set; } = new();
    public Mock<ISprintActivityLogRepository> Logs { get; set; } = new();
    public Mock<IProjectMemberRepository> Members { get; set; } = new();
    public Mock<IMilestoneMembershipCoordinator> Membership { get; set; } = new();
    public Mock<INotificationDispatcher> Notifications { get; set; } = new();
    public Mock<ICallerIdentityResolver> Identity { get; set; } = new();
    public Mock<IWorkHierarchyService> Hierarchy { get; } = new();
    public Mock<IWorkApprovalEngine> Approvals { get; } = new();
    public Mock<IWorkNotificationEngine> WorkNotifications { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public void EngineDirect() => SetupEngine(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

    public void EnginePending() => SetupEngine(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(RequestId, RootOwner)));

    private void SetupEngine(Result<ApprovalDecision> result)
        => Approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .Callback<WorkAction, CancellationToken>((a, _) => Submitted.Add(a)).ReturnsAsync(result);

    public SprintWriteService Writes() => new(Projects.Object, Sprints.Object, Tasks.Object, Statuses.Object, Assignment.Object,
        Logs.Object, new SprintAudienceResolver(Tasks.Object, Members.Object), Membership.Object, Notifications.Object);

    public SprintActionSubmitter Submitter() => new(Hierarchy.Object, Members.Object, Identity.Object, UnitOfWork.Object,
        Approvals.Object, WorkNotifications.Object);

    public void VerifyEngineNeverCalled()
        => Approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
}
