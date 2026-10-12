using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>The single source of truth for which unified Work Approval <see cref="WorkActionTypes"/>
/// My Team's V1 scope covers - the exact union every work.* ITeamActionSource filters by, and the
/// exact filter WorkLeadershipService.HasPendingWorkApprovalsAsync must also use. Both call sites
/// referencing this one set (rather than typing the list out twice) is what guarantees the
/// capability gate and the Action Center content can never disagree about which requests count
/// (backend merge plan §0/§4).
///
/// Deliberately excludes ModuleMemberAdd/ModuleMemberRemove (module membership was never tracked
/// by My Team before), TaskDelete and TaskStatusChange (no legacy request type existed for either),
/// and all 6 Sprint.* values (no prior approval tracking at all) - surfacing these would be a scope
/// expansion, not a porting detail, so they stay out until explicitly scoped in.</summary>
public static class MyTeamApprovalActionTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
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
    };

    /// <summary>The 6 WorkActionTypes.Module* values that back the single "work.objective_change"
    /// Action Center source (one My Team category, one legacy widget key, per the backend merge
    /// plan §3 mapping table).</summary>
    public static readonly IReadOnlySet<string> ObjectiveChange = new HashSet<string>
    {
        WorkActionTypes.ModuleEdit,
        WorkActionTypes.ModuleDelete,
        WorkActionTypes.ModuleTransfer,
        WorkActionTypes.ModuleAchieve,
        WorkActionTypes.ModuleUnachieve,
        WorkActionTypes.ModuleAllocationExtend
    };
}
