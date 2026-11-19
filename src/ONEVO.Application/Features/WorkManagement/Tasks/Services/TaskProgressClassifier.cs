namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public enum TaskProgressBucket
{
    Completed,
    Overdue,
    InProgress,
    NotStarted,
}

/// <summary>The one Completed / Overdue / In Progress / Not Started rule for Work tasks, shared by
/// the personal Task Progress widget (GetMyTaskProgressQueryHandler) and My Team's Team Progress
/// (My Team spec §8.3.3) so the two can never disagree.
///
/// V1 date/time assumption (review clarification, 2026-09-30): <paramref name="today"/> is the
/// UTC calendar date (DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)), exactly as
/// GetMyTaskProgressQueryHandler already computed it before this extraction - NOT the caller's
/// legal-entity-local date the way AttendanceScheduleResolver computes "today" for attendance.
/// This extraction preserves that existing behavior unchanged; it does not introduce or fix any
/// timezone normalization. A task due "today" in a legal entity many hours ahead of or behind UTC
/// can therefore flip between not-overdue and overdue a few hours earlier/later than a
/// legal-entity-local clock would show. Normalizing this to legal-entity-local time, if ever
/// wanted, is a separate, explicitly scoped follow-up - not part of this work.</summary>
public static class TaskProgressClassifier
{
    public static TaskProgressBucket Classify(bool marksTaskComplete, int progressPercent, DateOnly? dueDate, DateOnly today)
    {
        // A task can reach 100% via the clock-in Push flow without being dragged to a
        // MarksTaskComplete column - see GetMyActiveTasksAsync, which treats it as done too.
        if (marksTaskComplete || progressPercent >= 100)
            return TaskProgressBucket.Completed;
        if (dueDate is { } due && due < today)
            return TaskProgressBucket.Overdue;
        return progressPercent > 0 ? TaskProgressBucket.InProgress : TaskProgressBucket.NotStarted;
    }
}
