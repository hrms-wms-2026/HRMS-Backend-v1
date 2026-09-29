using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.RequestAllocationExtension;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.AchieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.EditObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.TransferObjectiveHead;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UnachieveObjective;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

/// <summary>
/// Shared wiring for the Module command handlers: a real ModuleWriteService and ModuleActionSubmitter
/// over mocks, with the approval engine mocked (Direct by default). The tree is Parent (owned by
/// ParentOwner) → Module (head = Head). The caller defaults to the Module's head.
/// </summary>
public sealed class ModuleHandlerTestKit
{
    public static readonly Guid TenantId = Guid.NewGuid();
    public static readonly Guid ProjectId = Guid.NewGuid();
    public static readonly Guid ParentId = Guid.NewGuid();
    public static readonly Guid ModuleId = Guid.NewGuid();
    public static readonly Guid ParentOwner = Guid.NewGuid();
    public static readonly Guid ParentOwnerUser = Guid.NewGuid();
    public static readonly Guid Head = Guid.NewGuid();
    public static readonly Guid HeadUser = Guid.NewGuid();
    public static readonly Guid Other = Guid.NewGuid();
    public static readonly Guid OtherUser = Guid.NewGuid();
    public static readonly Guid NewHead = Guid.NewGuid();
    public static readonly Guid NewHeadUser = Guid.NewGuid();
    public static readonly Guid RequestId = Guid.NewGuid();

    public Mock<ICurrentUser> CurrentUser { get; } = new();
    public Mock<ICallerIdentityResolver> Identity { get; } = new();
    public Mock<IObjectiveRepository> Objectives { get; } = new();
    public Mock<IMilestoneMembershipCoordinator> Membership { get; } = new();
    public Mock<ISprintRepository> Sprints { get; } = new();
    public Mock<IObjectiveAllocationSlackCalculator> Slack { get; } = new();
    public Mock<IWorkApprovalEngine> Approvals { get; } = new();
    public Mock<IWorkNotificationEngine> Notifications { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    public Mock<IProjectMemberInvitationRepository> Invitations { get; } = new();
    public Mock<IOutboxWriter> Outbox { get; } = new();
    public List<WorkAction> Submitted { get; } = new();
    public List<WorkNotificationEvent> Notified { get; } = new();

    public Objective Parent { get; } = new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, Title = "Parent", OwnerId = ParentOwner, IsActive = true,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), AllocatedHours = 100m
    };

    public Objective Module { get; }

    public ModuleHandlerTestKit(Objective? module = null)
    {
        Module = module ?? NewModule();

        CurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        CurrentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        CallAs(HeadUser);
        Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, HeadUser, It.IsAny<CancellationToken>())).ReturnsAsync(Head);
        Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUser, It.IsAny<CancellationToken>())).ReturnsAsync(Other);
        Identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, ParentOwnerUser, It.IsAny<CancellationToken>())).ReturnsAsync(ParentOwner);
        Identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        Objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Module);
        Objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Module);
        Objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(Parent);
        Objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        Membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ModuleId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid employeeId, CancellationToken _) => employeeId != Other || OtherIsMember);
        Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, Head, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Head, UserId = HeadUser });
        Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = NewHead, UserId = NewHeadUser });

        Slack.Setup(x => x.CalculateAsync(TenantId, Parent, null, It.IsAny<CancellationToken>())).ReturnsAsync(50m);

        Invitations.Setup(x => x.ListPendingForObjectiveAsync(TenantId, ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMemberInvitation>());

        EngineDirect();
        Notifications.Setup(x => x.NotifyAsync(It.IsAny<WorkNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkNotificationEvent, CancellationToken>((e, _) => Notified.Add(e)).Returns(Task.CompletedTask);

        UnitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ModuleActionOutcome>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ModuleActionOutcome>>> op, CancellationToken ct) => op(ct));
        UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    /// <summary>When false, the Other caller is not a member of the Module or any ancestor (403).</summary>
    public bool OtherIsMember { get; set; } = true;

    public static Objective NewModule(Guid? createdByUser = null) => new()
    {
        Id = ModuleId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, Title = "Module",
        OwnerId = Head, ReportingManagerId = ParentOwner, CreatedById = createdByUser ?? HeadUser, IsActive = true,
        StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 10m,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
    };

    public void CallAs(Guid userId) => CurrentUser.SetupGet(x => x.UserId).Returns(userId);

    public void EngineDirect() => SetupEngine(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));

    public void EnginePending() => SetupEngine(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(RequestId, ParentOwner)));

    public void EngineConflict() => SetupEngine(Result<ApprovalDecision>.Conflict("A request for this change is already waiting for approval."));

    private void SetupEngine(Result<ApprovalDecision> result)
        => Approvals.Setup(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .Callback<WorkAction, CancellationToken>((a, _) => Submitted.Add(a)).ReturnsAsync(result);

    public ModuleWriteService Writes() => new(Objectives.Object, Sprints.Object, Membership.Object, Slack.Object);

    public ModuleActionSubmitter Submitter() => new(Objectives.Object, UnitOfWork.Object, Approvals.Object, Notifications.Object);

    public EditObjectiveCommandHandler Edit()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Writes(), Submitter());

    public DeleteObjectiveCommandHandler Delete()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Writes(), Submitter());

    public AchieveObjectiveCommandHandler Achieve()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Writes(), Submitter());

    public UnachieveObjectiveCommandHandler Unachieve()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Writes(), Submitter());

    public TransferObjectiveHeadCommandHandler Transfer()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Invitations.Object, UnitOfWork.Object, Membership.Object,
            Outbox.Object, Writes(), Submitter());

    public RequestAllocationExtensionCommandHandler AllocationExtend()
        => new(CurrentUser.Object, Identity.Object, Objectives.Object, Membership.Object, Writes(), Submitter());

    public void VerifyEngineNeverCalled()
        => Approvals.Verify(x => x.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()), Times.Never);
}
