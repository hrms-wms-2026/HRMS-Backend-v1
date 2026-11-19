namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

/// <summary>A lighter task shape for the milestone Tasks tab and its Key Metrics tile - deliberately
/// not the full WorkTaskResponse (no assignees/clock-session plumbing needed here). StatusCategory
/// (not just the done flag) lets the UI break Key Metrics into not_started/active/done, not just
/// done-vs-everything-else.</summary>
public sealed record CalendarEventTaskSummaryResponse(
    Guid Id, string ShortId, string Title, Guid StatusId, bool MarksTaskComplete, string StatusCategory,
    int ProgressPercent, Guid ObjectiveId, string ObjectiveTitle);
