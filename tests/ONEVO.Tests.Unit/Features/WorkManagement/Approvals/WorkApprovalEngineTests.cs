using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class WorkApprovalEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();   // root owner
    private static readonly Guid A = Guid.NewGuid();      // owner of P
    private static readonly Guid Cc = Guid.NewGuid();     // owner of C (child of P)
    private static readonly Guid B = Guid.NewGuid();      // member
    private static readonly Guid HrManager = Guid.NewGuid();

    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly List<WorkApprovalRequest> _added = new();

    public WorkApprovalEngineTests()
    {
        var tree = new ProjectModuleTree(new[]
        {
            new Objective { Id = _root, OwnerId = Lead, IsDefault = true },
            new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
            new Objective { Id = _c, ParentObjectiveId = _p, OwnerId = Cc },
        });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _p, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(A);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _c, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cc);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _root, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Lead);
        _requests.Setup(x => x.AddAsync(It.IsAny<WorkApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WorkApprovalRequest, CancellationToken>((r, _) => _added.Add(r)).Returns(Task.CompletedTask);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, OwningLegalEntityId = LegalEntityId });
    }

    private WorkApprovalEngine Build() => new(_hierarchy.Object, _requests.Object, _notifications.Object, _projects.Object, _authority.Object);

    private WorkAction Action(Guid actor, string actionType, string targetType, Guid targetModuleId, Guid? position, Guid? targetId = null)
        => new(TenantId, ProjectId, actor, actionType, targetType, targetId ?? Guid.NewGuid(), "Thing",
            targetModuleId, position, "{}", null);

    [Fact]
    public async Task ChildModuleOwnerEditsOwnModule_RequestGoesToParentOwner()
    {
        // B/CC edits C, whose creator position is P → P's owner (A) approves.
        var result = await Build().SubmitAsync(Action(Cc, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c));

        result.Value!.IsDirect.Should().BeFalse();
        _added.Should().ContainSingle();
        _added[0].ApproverEmployeeId.Should().Be(A);
        _added[0].PositionObjectiveId.Should().Be(_p);
        _added[0].ApproverSource.Should().Be(WorkApprovalSources.Hierarchy);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Requested && e.RecipientEmployeeIds.Single() == A), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task PositionHolderOrAncestor_IsDirect_NoRequest()
    {
        (await Build().SubmitAsync(Action(A, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c))).Value!.IsDirect.Should().BeTrue();
        (await Build().SubmitAsync(Action(Lead, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c))).Value!.IsDirect.Should().BeTrue();
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task MemberCreatesTask_RequestGoesToModuleOwner()
    {
        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskCreate, WorkTargetTypes.Task, _c, null) with { TargetId = null });

        result.Value!.IsDirect.Should().BeFalse();
        _added[0].ApproverEmployeeId.Should().Be(Cc);
        _added[0].TargetId.Should().BeNull();
    }

    [Fact]
    public async Task NullPosition_TaskFallsBackToTargetModule()
    {
        (await Build().SubmitAsync(Action(Cc, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, null))).Value!.IsDirect.Should().BeTrue();
    }

    [Fact]
    public async Task RootOwnerEditsRoot_GoesToHrReportingApprover()
    {
        _authority.Setup(x => x.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == Lead && r.LegalEntityId == LegalEntityId && r.RequiredPermission == "projects:access"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                HrManager, Guid.NewGuid(), Guid.NewGuid(), "projects:access", EmployeeAuthorityPurpose.EmployeeLifecycleApproval, EmployeeApprovalRouteSource.ReportingLine, null)));

        var result = await Build().SubmitAsync(Action(Lead, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _root, null, _root));

        result.Value!.IsDirect.Should().BeFalse();
        _added[0].ApproverEmployeeId.Should().Be(HrManager);
        _added[0].ApproverSource.Should().Be(WorkApprovalSources.Hr);
        _added[0].PositionObjectiveId.Should().BeNull();
    }

    [Fact]
    public async Task SomeoneElseEditsRoot_RootOwnerApproves()
    {
        await Build().SubmitAsync(Action(A, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _root, null, _root));
        _added[0].ApproverEmployeeId.Should().Be(Lead);
    }

    [Fact]
    public async Task NoHolderAndNoHrApprover_Returns422()
    {
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), _c, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _authority.Setup(x => x.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("none"));

        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, _c));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicatePending_Returns409()
    {
        var targetId = Guid.NewGuid();
        _requests.Setup(x => x.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, _c, targetId));

        result.StatusCode.Should().Be(409);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownPositionModule_Returns404()
    {
        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, Guid.NewGuid()));
        result.StatusCode.Should().Be(404);
    }
}
