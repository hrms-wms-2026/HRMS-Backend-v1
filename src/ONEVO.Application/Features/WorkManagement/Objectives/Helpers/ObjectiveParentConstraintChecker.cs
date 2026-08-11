using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Helpers;

/// <summary>
/// The design's §4/§8 conflict rule: a child's date range must fall within its parent's (inclusive
/// bounds - touching the boundary is not a conflict), and the child's allocated hours must not
/// exceed the parent's total allocated hours (not remaining headroom after siblings - deliberately
/// simple, matching phase1-table-inventory.md's existing warning-only treatment of hours elsewhere).
/// Used both by Create (reject out-of-bounds children outright) and Edit (route a conflicting
/// change through approval instead of applying it).
/// </summary>
public static class ObjectiveParentConstraintChecker
{
    public static bool Conflicts(Objective parent, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var datesOutOfRange = startDate < parent.StartDate || endDate > parent.EndDate;
        var hoursExceeded = allocatedHours > parent.AllocatedHours;
        return datesOutOfRange || hoursExceeded;
    }
}
