namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record TeamsMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record TeamsAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public interface ITeamsMeetingClient
{
    /// <summary>Graph POST /me/onlineMeetings/createOrGet: returns the organizer's existing meeting with
    /// this externalId, or creates it. Throws MeetingProviderException on failure.</summary>
    /// <remarks><paramref name="attendeeEmails"/> are added to the meeting (plan C1 P2-3) so they are recognised when they join.</remarks>
    Task<TeamsMeetingDto> CreateOrGetMeetingAsync(
        string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct,
        IReadOnlyList<string>? attendeeEmails = null);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, DateTimeOffset sessionStart, DateTimeOffset sessionEnd, CancellationToken ct);
}
