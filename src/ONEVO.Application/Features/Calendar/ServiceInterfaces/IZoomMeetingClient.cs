namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Zoom rejects deleting a meeting that has already started (error code 3002) - thrown so
/// RemoveEventMeetingCommandHandler can surface a friendly, actionable message instead of a
/// generic 500 from an unhandled HttpRequestException.</summary>
public sealed class ZoomMeetingInProgressException() : Exception("This Zoom meeting is currently in progress and can't be deleted.");

public sealed record ZoomMeetingDto(
    string ExternalMeetingId, string JoinUrl, string? OrganizerJoinUrl, string? PasscodeOrPin);

public sealed record ZoomAttendanceRecordDto(
    string? ParticipantName, string? ParticipantEmail, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public interface IZoomMeetingClient
{
    /// <summary>Creates a scheduled meeting whose agenda is <paramref name="correlationToken"/>.
    /// Throws MeetingProviderException on failure.</summary>
    /// <remarks><paramref name="attendeeEmails"/> are added to the meeting (plan C1 P2-3) so they are recognised when they join.</remarks>
    Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, string correlationToken, CancellationToken ct,
        IReadOnlyList<string>? attendeeEmails = null);

    /// <summary>The organizer's scheduled meeting whose agenda is exactly <paramref name="correlationToken"/>,
    /// or null when every page was read and none matches. Throws MeetingProviderException when the list
    /// can't be read completely (then nothing is known).</summary>
    Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(
        string accessToken, string correlationToken, DateTimeOffset eventStart, CancellationToken ct);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, DateTimeOffset sessionStart, DateTimeOffset sessionEnd, CancellationToken ct);
}
