using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;
using ObjectiveReverters = ONEVO.Application.Features.WorkManagement.Objectives.Reverters;
using SprintReverters = ONEVO.Application.Features.WorkManagement.Sprints.Reverters;
using TaskReverters = ONEVO.Application.Features.WorkManagement.Tasks.Reverters;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalActionReverterRegistryTests
{
    private sealed class FakeReverter : IApprovalActionReverter
    {
        public string ActionType { get; init; } = WorkActionTypes.ModuleEdit;
        public Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
            => Task.FromResult(RevertOutcome.Reverted());
    }

    [Fact]
    public void Find_ReturnsRegisteredReverter_ByActionType()
    {
        var registry = new ApprovalActionReverterRegistry(new IApprovalActionReverter[] { new FakeReverter() });
        registry.Find(WorkActionTypes.ModuleEdit).Should().NotBeNull();
        registry.Find(WorkActionTypes.TaskDelete).Should().BeNull();
    }

    [Fact]
    public void Constructor_TwoRevertersForSameActionType_Throws()
    {
        var act = () => new ApprovalActionReverterRegistry(new IApprovalActionReverter[]
        {
            new FakeReverter(), new FakeReverter()
        });
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Mirrors the 18 IApprovalActionReverter registrations in DependencyInjection.cs exactly -
    /// a forgotten reverter class (not just a forgotten DI line) fails here instead of in production,
    /// where a missing reverter degrades gracefully to 422 (RevertWorkApprovalRequestCommandHandler's
    /// "no reverter registered" branch) but never corrupts data.</summary>
    [Fact]
    public void EveryActionTypeWithAnApplier_AlsoHasAReverter()
    {
        var taskWrites = new Mock<ITaskWriteService>().Object;
        var objectives = new Mock<IObjectiveRepository>().Object;
        var tasks = new Mock<IWorkTaskRepository>().Object;
        var statuses = new Mock<ITaskStatusRepository>().Object;
        var moduleWrites = new Mock<IModuleWriteService>().Object;
        var membership = new Mock<IMilestoneMembershipCoordinator>().Object;
        var assignments = new Mock<ITaskAssignmentRepository>().Object;
        var sprintWrites = new Mock<ISprintWriteService>().Object;
        var sprints = new Mock<ISprintRepository>().Object;

        var reverters = new IApprovalActionReverter[]
        {
            new TaskReverters.TaskCreateReverter(taskWrites, tasks),
            new TaskReverters.TaskEditReverter(taskWrites, objectives, tasks),
            new TaskReverters.TaskDeleteReverter(taskWrites, tasks),
            new TaskReverters.ProjectStatusTemplateChangeReverter(statuses, tasks),
            new ObjectiveReverters.ModuleAllocationExtendReverter(objectives),
            new ObjectiveReverters.ModuleMemberAddReverter(objectives, membership, assignments),
            new ObjectiveReverters.ModuleMemberRemoveReverter(objectives, membership),
            new ObjectiveReverters.ModuleUnachieveReverter(moduleWrites, objectives),
            new ObjectiveReverters.ModuleAchieveReverter(moduleWrites, objectives),
            new ObjectiveReverters.ModuleTransferReverter(moduleWrites, objectives),
            new ObjectiveReverters.ModuleDeleteReverter(moduleWrites, objectives),
            new ObjectiveReverters.ModuleEditReverter(moduleWrites, objectives),
            new SprintReverters.SprintDeleteReverter(sprintWrites, sprints),
            new SprintReverters.SprintAchieveReverter(sprintWrites, sprints),
            new SprintReverters.SprintCompleteReverter(sprintWrites, sprints),
            new SprintReverters.SprintStartReverter(sprints),
            new SprintReverters.SprintEditReverter(sprintWrites, sprints),
            new SprintReverters.SprintCreateReverter(sprintWrites, sprints),
        };
        var registry = new ApprovalActionReverterRegistry(reverters);

        var actionTypesWithAppliers = new[]
        {
            WorkActionTypes.TaskCreate, WorkActionTypes.TaskEdit, WorkActionTypes.TaskDelete,
            WorkActionTypes.ProjectStatusTemplateChange, WorkActionTypes.ModuleAllocationExtend,
            WorkActionTypes.ModuleMemberAdd, WorkActionTypes.ModuleMemberRemove, WorkActionTypes.ModuleUnachieve,
            WorkActionTypes.ModuleAchieve, WorkActionTypes.ModuleTransfer, WorkActionTypes.ModuleDelete,
            WorkActionTypes.ModuleEdit, WorkActionTypes.SprintDelete, WorkActionTypes.SprintAchieve,
            WorkActionTypes.SprintComplete, WorkActionTypes.SprintStart, WorkActionTypes.SprintEdit,
            WorkActionTypes.SprintCreate,
        };
        reverters.Should().HaveCount(actionTypesWithAppliers.Length);

        foreach (var actionType in actionTypesWithAppliers)
            registry.Find(actionType).Should().NotBeNull($"'{actionType}' has a registered IApprovalActionApplier and should have a matching reverter");
    }
}
