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
    /// <remarks><paramref name="attendeeEmails"/> are added to the meeting (plan C1 P2-3) so they are recognised when they join.</remarks>
    Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct,
        IReadOnlyList<string>? attendeeEmails = null);
    Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct);
    Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct);
}
