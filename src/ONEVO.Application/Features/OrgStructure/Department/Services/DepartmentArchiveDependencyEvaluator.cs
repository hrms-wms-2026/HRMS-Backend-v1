using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Services;

// Position has no DepartmentId/LegalEntityId/status column yet (see Position.cs - Name and
// DefaultRoleId only), so there is no schema representation of "a position belongs to this
// department." ActivePositionCount is therefore always 0 and PositionDependencyCheckSupported
// is always false - a documented schema limitation, not an unverified guess: positions cannot
// be linked to a department at all today, so 0 is the only value that could ever be measured.
public static class DepartmentArchiveDependencyEvaluator
{
    public static async Task<DepartmentArchiveBlockers> EvaluateAsync(
        IDepartmentRepository departments,
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        CancellationToken ct)
    {
        var activeSubdepartmentCount = await departments.CountActiveChildrenAsync(
            tenantId, legalEntityId, departmentId, ct);
        var activeEmployeeCount = await departments.CountActiveEmployeesAsync(
            tenantId, legalEntityId, departmentId, ct);

        return new DepartmentArchiveBlockers(
            ActiveSubdepartmentCount: activeSubdepartmentCount,
            ActiveEmployeeCount: activeEmployeeCount,
            ActivePositionCount: 0,
            IsUsedAsParent: activeSubdepartmentCount > 0,
            HasActiveEmployees: activeEmployeeCount > 0,
            HasActivePositions: false,
            PositionDependencyCheckSupported: false);
    }

    public static bool CanArchive(DepartmentArchiveBlockers blockers)
    {
        return blockers.ActiveSubdepartmentCount == 0 && blockers.ActiveEmployeeCount == 0;
    }

    public static string BuildMessage(DepartmentArchiveBlockers blockers)
    {
        if (CanArchive(blockers))
        {
            return "No active employees, positions, or subdepartments are linked to this department.";
        }

        var reasons = new List<string>();
        if (blockers.ActiveSubdepartmentCount > 0)
        {
            reasons.Add("subdepartments");
        }
        if (blockers.ActiveEmployeeCount > 0)
        {
            reasons.Add("employees");
        }

        var joined = reasons.Count == 1 ? reasons[0] : string.Join(" and ", reasons);
        return $"This department cannot be archived yet. Reassign linked {joined} first.";
    }
}
