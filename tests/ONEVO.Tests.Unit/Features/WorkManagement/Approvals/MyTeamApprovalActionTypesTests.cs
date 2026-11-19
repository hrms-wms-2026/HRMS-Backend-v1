using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public sealed class MyTeamApprovalActionTypesTests
{
    [Fact]
    public void All_ContainsExactlyTheNineInScopeActionTypes()
    {
        MyTeamApprovalActionTypes.All.Should().BeEquivalentTo(new[]
        {
            WorkActionTypes.TaskCreate,
            WorkActionTypes.TaskEdit,
            WorkActionTypes.ModuleEdit,
            WorkActionTypes.ModuleDelete,
            WorkActionTypes.ModuleTransfer,
            WorkActionTypes.ModuleAchieve,
            WorkActionTypes.ModuleUnachieve,
            WorkActionTypes.ModuleAllocationExtend,
            WorkActionTypes.ProjectStatusTemplateChange
        });
    }

    [Fact]
    public void All_ExcludesTheTenOutOfScopeActionTypes()
    {
        var excluded = new[]
        {
            WorkActionTypes.TaskDelete,
            WorkActionTypes.TaskStatusChange,
            WorkActionTypes.ModuleMemberAdd,
            WorkActionTypes.ModuleMemberRemove,
            WorkActionTypes.SprintCreate,
            WorkActionTypes.SprintEdit,
            WorkActionTypes.SprintDelete,
            WorkActionTypes.SprintStart,
            WorkActionTypes.SprintComplete,
            WorkActionTypes.SprintAchieve
        };

        excluded.Should().HaveCount(10);
        foreach (var actionType in excluded)
            MyTeamApprovalActionTypes.All.Should().NotContain(actionType);
    }

    [Fact]
    public void ObjectiveChange_IsExactlyTheSixModuleTypes_AndASubsetOfAll()
    {
        MyTeamApprovalActionTypes.ObjectiveChange.Should().BeEquivalentTo(new[]
        {
            WorkActionTypes.ModuleEdit,
            WorkActionTypes.ModuleDelete,
            WorkActionTypes.ModuleTransfer,
            WorkActionTypes.ModuleAchieve,
            WorkActionTypes.ModuleUnachieve,
            WorkActionTypes.ModuleAllocationExtend
        });

        foreach (var actionType in MyTeamApprovalActionTypes.ObjectiveChange)
            MyTeamApprovalActionTypes.All.Should().Contain(actionType);
    }
}
