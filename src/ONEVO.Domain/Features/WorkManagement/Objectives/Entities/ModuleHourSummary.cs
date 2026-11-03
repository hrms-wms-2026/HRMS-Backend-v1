using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

/// <summary>
/// Pre-computed estimated-hours totals of ONE Module's own top-level tasks (not its child Modules),
/// so My Team's Team Progress reads one small row per Module instead of aggregating every task on
/// each poll. Id is the Module (Objective) id.
///
/// Written only by the database: the wm_refresh_module_hour_summary trigger function recomputes a
/// row whenever a task in that Module is inserted, deleted, or has its status, estimate, progress,
/// due date, parent or Module changed, and when a task status's MarksTaskComplete flips
/// (migration AddModuleHourSummaries). The application never writes it. A Module with no row yet
/// simply has no tasks. Buckets follow TaskProgressClassifier without the date rule: Completed
/// (MarksTaskComplete status or 100%), In progress (&gt; 0%), otherwise Not started.
/// </summary>
public class ModuleHourSummary : BaseEntity
{
    public Guid ProjectId { get; set; }
    public decimal NotStartedHours { get; set; }
    public decimal InProgressHours { get; set; }
    public decimal CompletedHours { get; set; }
    public int TaskCount { get; set; }
    /// <summary>Top-level tasks with no (or zero) estimate - they add no hours to any bucket.</summary>
    public int UnestimatedTaskCount { get; set; }
    /// <summary>Earliest due date among the Module's not-completed tasks; before today = overdue work.</summary>
    public DateOnly? EarliestOpenDueDate { get; set; }
}
