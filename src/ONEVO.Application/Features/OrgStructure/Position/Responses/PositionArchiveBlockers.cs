namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// ActiveOccupants is nullable, not defaulted to 0: no position_assignments/employee-position
// table exists anywhere in this codebase (confirmed by a repo-wide search), so the count is
// genuinely unmeasurable today, not zero. ActiveOccupantsCheckSupported=false documents that
// limitation explicitly rather than faking a verified zero. CanArchive intentionally excludes
// ActiveOccupants from the gate for the same reason: an unverifiable count cannot block anything.
public record PositionArchiveBlockers(
    int? ActiveOccupants,
    bool ActiveOccupantsCheckSupported,
    int HeadOfDepartments,
    int ActiveChildPositions)
{
    public bool CanArchive => HeadOfDepartments == 0 && ActiveChildPositions == 0;
}
