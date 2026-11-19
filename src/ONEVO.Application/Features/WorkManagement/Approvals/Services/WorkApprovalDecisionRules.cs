using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Who may approve or reject a pending request, evaluated at decision time (spec §5.3).</summary>
public static class WorkApprovalDecisionRules
{
    public static bool CanDecide(ProjectModuleTree tree, WorkApprovalRequest request, Guid employeeId)
    {
        if (employeeId == request.RequestedByEmployeeId)
            return false;

        if (request.ApproverSource == WorkApprovalSources.Hr || request.PositionObjectiveId is null)
            return employeeId == request.ApproverEmployeeId;

        // Hierarchy: the position follows transfers, so the check is re-run against today's tree.
        // The stored ApproverEmployeeId can still decide when it is an ancestor that was picked
        // because the position owner was inactive - IsAtOrAbove already covers that.
        return tree.IsAtOrAbove(employeeId, request.PositionObjectiveId.Value);
    }
}
