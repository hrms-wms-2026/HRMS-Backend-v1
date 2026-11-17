using ONEVO.Application.Features.WorkManagement;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>The Work Management gate for overview signals: any one of these modules enables the
/// overdue-tasks signal and its drill-down.</summary>
public static class EmployeeOverviewWorkModules
{
    private static readonly string[] Keys =
        [WorkManagementModule.Key];

    public static async Task<bool> IsEnabledAsync(IModuleEntitlementService modules, Guid tenantId, CancellationToken ct)
    {
        foreach (var key in Keys)
            if (await modules.IsModuleEnabledAsync(tenantId, key, ct))
                return true;
        return false;
    }
}
