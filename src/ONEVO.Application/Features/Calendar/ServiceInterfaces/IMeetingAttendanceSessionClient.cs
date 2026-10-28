namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>One attendance interval of one participant in a provider session.</summary>
/// <param name="ParticipantKey">Stable per participant within the session: the Teams record id (or its identity id); for
/// Zoom <c>participant_user_id ?? id ?? lower(email) ?? name</c>.</param>
public sealed record ProviderAttendanceRecord(string ParticipantKey, string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

/// <summary>One held session of a provider meeting - a Teams attendance report or a Zoom past instance - with its id, so a
/// reused link's sessions can each be assigned to one occurrence (spec 5.8).</summary>
/// <param name="End">Teams: the report's end; Zoom: the latest participant leave time. Null while unknown.</param>
public sealed record ProviderAttendanceSession(string ProviderSessionId, DateTimeOffset Start, DateTimeOffset? End,
    IReadOnlyList<ProviderAttendanceRecord> Records);

/// <summary>Lists a provider meeting's sessions for per-occurrence attendance sync. Registered once per provider.</summary>
public interface IMeetingAttendanceSessionClient
{
    /// <summary>A CalendarEventMeetingProviders value.</summary>
    string Provider { get; }

    /// <summary>Every session whose start lies in [<paramref name="from"/>, <paramref name="to"/>], oldest first, with all of
    /// its records. Throws <see cref="MeetingProviderException"/> on any provider failure.</summary>
    Task<IReadOnlyList<ProviderAttendanceSession>> ListSessionsAsync(string accessToken, string externalMeetingId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}
