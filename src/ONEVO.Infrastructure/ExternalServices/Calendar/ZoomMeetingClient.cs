using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class ZoomMeetingClient(HttpClient httpClient, ILogger<ZoomMeetingClient> logger) : IZoomMeetingClient
{
    public async Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.zoom.us/v2/users/me/meetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var durationMinutes = Math.Max(1, (int)(end - start).TotalMinutes);
        request.Content = JsonContent.Create(new
        {
            topic = subject,
            type = 2, // scheduled meeting
            start_time = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            duration = durationMinutes,
            timezone = "UTC",
            settings = new { join_before_host = false, waiting_room = true }
        });
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "create meeting", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetInt64().ToString();
        var joinUrl = root.GetProperty("join_url").GetString()!;
        var organizerJoinUrl = root.TryGetProperty("start_url", out var startUrl) ? startUrl.GetString() : null;
        var passcode = root.TryGetProperty("password", out var pwd) ? pwd.GetString() : null;

        return new ZoomMeetingDto(externalMeetingId, joinUrl, organizerJoinUrl, passcode);
    }

    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.zoom.us/v2/meetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "cancel meeting", ct);
    }

    public async Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Zoom's past-meeting participants report, the direct analogue of Microsoft Graph's
        // attendanceReports endpoint — see MicrosoftGraphMeetingClient.GetAttendanceAsync for the
        // Teams equivalent. Phase 2 takes only the first page (up to Zoom's default page size),
        // matching Teams' own simplification of "non-recurring single-session events only".
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.zoom.us/v2/past_meetings/{externalMeetingId}/participants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "list past meeting participants", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<ZoomAttendanceRecordDto>();
        foreach (var participant in doc.RootElement.GetProperty("participants").EnumerateArray())
        {
            var name = participant.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = participant.TryGetProperty("user_email", out var e) ? e.GetString() : null;
            var joined = participant.GetProperty("join_time").GetDateTimeOffset();
            var left = participant.TryGetProperty("leave_time", out var l) && l.ValueKind != JsonValueKind.Null
                ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
            result.Add(new ZoomAttendanceRecordDto(name, email, joined, left));
        }
        return result;
    }

    /// <summary>Same reasoning as MicrosoftGraphMeetingClient.EnsureSuccessOrLogAsync — logs
    /// Zoom's actual error body (invalid field, missing scope, licensing issue) before throwing,
    /// since EnsureSuccessStatusCode() alone discards it.</summary>
    private async Task EnsureSuccessOrLogAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Zoom {Operation} returned {StatusCode}: {ErrorBody}",
                operation, (int)response.StatusCode, errorBody);
        }
        response.EnsureSuccessStatusCode();
    }
}
