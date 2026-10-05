using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>The Work Management gate for overview signals: any one of these modules enables the
/// overdue-tasks signal and its drill-down.</summary>
public static class EmployeeOverviewWorkModules
{
    private static readonly string[] Keys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public static async Task<bool> IsEnabledAsync(IModuleEntitlementService modules, Guid tenantId, CancellationToken ct)
    {
        foreach (var key in Keys)
            if (await modules.IsModuleEnabledAsync(tenantId, key, ct))
                return true;
        return false;
    }
}
