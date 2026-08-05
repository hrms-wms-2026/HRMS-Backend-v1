using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Services;

// No position_assignments/employee-position table exists anywhere in this codebase (confirmed
// by a repo-wide search), so ActiveOccupants is always reported as null/unsupported rather than
// a fabricated 0 - a documented schema limitation, not an unverified guess, mirroring
// DepartmentArchiveDependencyEvaluator's precedent for PositionDependencyCheckSupported.
public static class PositionArchiveDependencyEvaluator
{
    public static async Task<PositionArchiveBlockers> EvaluateAsync(
        IPositionRepository positions,
        IDepartmentRepository departments,
        Guid tenantId,
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct)
    {
        var activeChildPositions = await positions.CountActiveReportsToPositionAsync(
            tenantId, legalEntityId, positionId, ct);
        var headOfDepartments = await positions.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, positionId, ct);

        return new PositionArchiveBlockers(
            ActiveOccupants: null,
            ActiveOccupantsCheckSupported: false,
            HeadOfDepartments: headOfDepartments,
            ActiveChildPositions: activeChildPositions);
    }

    public static bool CanArchive(PositionArchiveBlockers blockers)
    {
        return blockers.CanArchive;
    }

    public static string BuildMessage(PositionArchiveBlockers blockers)
    {
        if (blockers.CanArchive)
        {
            return "No active child positions or department-head references are linked to this position.";
        }

        var reasons = new List<string>();
        if (blockers.ActiveChildPositions > 0)
        {
            reasons.Add("child positions");
        }
        if (blockers.HeadOfDepartments > 0)
        {
            reasons.Add("department head assignments");
        }

        var joined = reasons.Count == 1 ? reasons[0] : string.Join(" and ", reasons);
        return $"This position cannot be archived yet. Resolve linked {joined} first.";
    }
}
