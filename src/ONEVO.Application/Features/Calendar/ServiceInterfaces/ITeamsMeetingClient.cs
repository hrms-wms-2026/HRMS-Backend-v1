namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record TeamsMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record TeamsAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public interface ITeamsMeetingClient
{
    Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
