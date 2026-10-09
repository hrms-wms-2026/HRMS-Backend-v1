using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class WorkApprovalEligibilityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _module = Guid.NewGuid();

    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();

    public WorkApprovalEligibilityTests()
    {
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _module, ParentObjectiveId = _root, OwnerId = Owner },
            }));
    }

    private WorkApprovalRequest Pending(Guid position, string actionType) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = position,
        ApproverSource = WorkApprovalSources.Hierarchy, RequestedByEmployeeId = Requester,
        Status = WorkApprovalRequestStatuses.Pending, ActionType = actionType,
        TargetType = WorkTargetTypes.Task, TargetTitle = "t"
    };

    private WorkApprovalEligibility Build() => new(_requests.Object, _hierarchy.Object, _objectives.Object);

    [Fact]
    public async Task ReturnsOnlyRequests_CallerCanDecide_AndWithinActionTypes()
    {
        var decidable = Pending(_module, WorkActionTypes.TaskEdit);
        var wrongActionType = Pending(_module, WorkActionTypes.SprintCreate);
        var notOwned = Pending(_root, WorkActionTypes.TaskEdit);
        notOwned.RequestedByEmployeeId = Guid.NewGuid();
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { decidable, wrongActionType, notOwned });

        var result = await Build().ListDecidableAsync(
            TenantId, ProjectId, Owner, new HashSet<string> { WorkActionTypes.TaskEdit });

        result.Select(r => r.Id).Should().Equal(decidable.Id);
    }

    [Fact]
    public async Task NoPendingRequests_ReturnsEmpty()
    {
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest>());

        var result = await Build().ListDecidableAsync(TenantId, ProjectId, Owner, new HashSet<string> { WorkActionTypes.TaskEdit });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AcrossLedProjects_NoOwnedModules_ReturnsEmptyWithoutQueryingPendingOrTrees()
    {
        _objectives.Setup(x => x.ListActiveOwnedIdsAsync(TenantId, Owner, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid)>());

        var result = await Build().ListDecidableAcrossLedProjectsAsync(TenantId, Owner, LegalEntityId, new HashSet<string> { WorkActionTypes.TaskEdit });

        result.Should().BeEmpty();
        _hierarchy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AcrossLedProjects_NarrowsToProjectsWithPending_BeforeLoadingAnyTree()
    {
        var ownedWithNothingPending = Guid.NewGuid();
        _objectives.Setup(x => x.ListActiveOwnedIdsAsync(TenantId, Owner, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (Guid.NewGuid(), ownedWithNothingPending), (Guid.NewGuid(), ProjectId) });
        var actionTypes = new HashSet<string> { WorkActionTypes.TaskEdit };
        _requests.Setup(x => x.ListProjectsWithPendingAsync(
                TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), actionTypes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { ProjectId });
        var decidable = Pending(_module, WorkActionTypes.TaskEdit);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { decidable });

        var result = await Build().ListDecidableAcrossLedProjectsAsync(TenantId, Owner, LegalEntityId, actionTypes);

        result.Select(r => r.Id).Should().Equal(decidable.Id);
        _hierarchy.Verify(x => x.LoadTreeAsync(TenantId, ownedWithNothingPending, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcrossLedProjects_AggregatesDecidableRequestsFromEveryMatchingProject()
    {
        var secondProjectId = Guid.NewGuid();
        var secondModule = Guid.NewGuid();
        _objectives.Setup(x => x.ListActiveOwnedIdsAsync(TenantId, Owner, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (_module, ProjectId), (secondModule, secondProjectId) });
        var actionTypes = new HashSet<string> { WorkActionTypes.TaskEdit };
        _requests.Setup(x => x.ListProjectsWithPendingAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), actionTypes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { ProjectId, secondProjectId });
        var firstDecidable = Pending(_module, WorkActionTypes.TaskEdit);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { firstDecidable });
        var secondDecidable = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = secondProjectId, PositionObjectiveId = secondModule,
            ApproverSource = WorkApprovalSources.Hierarchy, RequestedByEmployeeId = Requester,
            Status = WorkApprovalRequestStatuses.Pending, ActionType = WorkActionTypes.TaskEdit,
            TargetType = WorkTargetTypes.Task, TargetTitle = "t2"
        };
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, secondProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[] { new Objective { Id = secondModule, OwnerId = Owner, IsDefault = true } }));
        _requests.Setup(x => x.ListByProjectAsync(TenantId, secondProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { secondDecidable });

        var result = await Build().ListDecidableAcrossLedProjectsAsync(TenantId, Owner, LegalEntityId, actionTypes);

        result.Select(r => r.Id).Should().BeEquivalentTo(new[] { firstDecidable.Id, secondDecidable.Id });
    }
}
