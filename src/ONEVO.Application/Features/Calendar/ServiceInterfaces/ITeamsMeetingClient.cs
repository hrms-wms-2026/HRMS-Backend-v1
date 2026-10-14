namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record TeamsMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record TeamsAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

/// <summary><see cref="ReportExists"/> = false means Teams has not produced an attendance report
/// yet (retry); true with no records means the report is final and nobody joined.</summary>
public sealed record TeamsAttendanceResult(bool ReportExists, IReadOnlyList<TeamsAttendanceRecordDto> Records);

public interface ITeamsMeetingClient
{
    /// <summary>Graph POST /me/onlineMeetings/createOrGet: returns the organizer's existing meeting with
    /// this externalId, or creates it. Throws MeetingProviderException on failure.</summary>
    Task<TeamsMeetingDto> CreateOrGetMeetingAsync(
        string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<TeamsAttendanceResult> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
