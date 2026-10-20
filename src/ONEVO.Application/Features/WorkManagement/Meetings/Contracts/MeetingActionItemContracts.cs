namespace ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

/// <summary>
/// One action item of a meeting occurrence: an ordinary task linked to the occurrence it was raised from
/// (spec 5.4, D14). <paramref name="LinkedByEmployeeId"/> is the employee who raised it (the link's creator);
/// <paramref name="LinkedAt"/> is when the link was written.
/// </summary>
public sealed record MeetingActionItemView(
    Guid TaskId,
    string ShortId,
    string Title,
    string StatusName,
    Guid ObjectiveId,
    Guid? AssigneeEmployeeId,
    Guid LinkedByEmployeeId,
    DateTimeOffset LinkedAt);
