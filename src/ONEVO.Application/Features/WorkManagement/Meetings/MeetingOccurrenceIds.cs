using ONEVO.Application.Common.Helpers;

namespace ONEVO.Application.Features.WorkManagement.Meetings;

/// <summary>
/// The id an occurrence gets when it is first materialized: a function of (series, original start), so a virtual
/// calendar occurrence and the task it later becomes share one id (meetings-as-tasks Phase 2 spec 5.5). Same scheme
/// as the calendar's virtual occurrence ids (GetCalendarEventsQueryHandler); the start is normalized to UTC so the
/// same instant in any offset gives the same id.
/// </summary>
public static class MeetingOccurrenceIds
{
    public static readonly Guid Namespace = Guid.Parse("3d6b1c8e-2f4a-4e9b-8a71-5c0d9e2f7b13");

    public static Guid For(Guid seriesId, DateTimeOffset originalStart)
        => DeterministicGuid.Create(Namespace, $"{seriesId:N}|{originalStart.ToUniversalTime():O}");
}
