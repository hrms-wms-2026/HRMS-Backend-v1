using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class ZoomMeetingClient(HttpClient httpClient, ILogger<ZoomMeetingClient> logger) : IZoomMeetingClient
{
    // Zoom rejects scheduled meetings longer than 1440 minutes (24 hours) with a 400. Clamping here
    // prevents a multi-day calendar event from turning into an uncaught HttpRequestException (500);
    // it does not attempt to solve what a genuinely multi-day Zoom meeting should look like.
    private const int MaxDurationMinutes = 1440;

    public async Task<ZoomMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, string correlationToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.zoom.us/v2/users/me/meetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var durationMinutes = Math.Clamp((int)(end - start).TotalMinutes, 1, MaxDurationMinutes);
        request.Content = JsonContent.Create(new
        {
            topic = subject,
            agenda = correlationToken,
            type = 2, // scheduled meeting
            // InvariantCulture is required: ':' in a custom .NET format string is a culture-dependent
            // time-separator placeholder, so on a host whose culture uses a non-':' time separator this
            // would silently emit the wrong string (see MicrosoftGraphCalendarClient for the same fix).
            start_time = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            duration = durationMinutes,
            timezone = "UTC",
            settings = new { join_before_host = false, waiting_room = true }
        });
        using var response = await SendClassifiedAsync(request, "create meeting", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetInt64().ToString();
        var joinUrl = root.GetProperty("join_url").GetString()!;
        // Deliberately never read Zoom's "start_url": it is a JWT (ZAK token) that routinely exceeds
        // the organizer_join_url column's varchar(500) limit, causing meeting creation to fail AFTER
        // the real Zoom meeting was already created remotely (orphaning it). It is also a host
        // credential - anyone holding it can start the meeting as host - that would be stored
        // unencrypted, unlike the encrypted token columns elsewhere in this schema.
        string? organizerJoinUrl = null;
        var passcode = root.TryGetProperty("password", out var pwd) ? pwd.GetString() : null;

        return new ZoomMeetingDto(externalMeetingId, joinUrl, organizerJoinUrl, passcode);
    }

    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.zoom.us/v2/meetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom cancel meeting failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, "Zoom cancel meeting timed out.", innerException: ex);
        }

        using (response)
        {
            // 404: already deleted - the goal state, not a failure.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return;

            // Zoom code 3002 ("Meeting is in progress"): user-recoverable, kept as its own exception.
            if (await TryReadZoomErrorCodeAsync(response, ct) == 3002)
            {
                await LogErrorBodyAsync(response, "cancel meeting", ct);
                throw new ZoomMeetingInProgressException();
            }

            await LogErrorBodyAsync(response, "cancel meeting", ct);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode), $"Zoom cancel meeting returned {(int)response.StatusCode}.",
                RetryAfterOf(response));
        }
    }

    private const int MaxListPages = 100;

    public async Task<ZoomMeetingDto?> FindMeetingByCorrelationTokenAsync(
        string accessToken, string correlationToken, DateTimeOffset eventStart, CancellationToken ct)
    {
        string? pageToken = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxListPages)
                throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom meeting list exceeded {MaxListPages} pages.");

            var url = "https://api.zoom.us/v2/users/me/meetings?type=scheduled&page_size=300"
                + (string.IsNullOrEmpty(pageToken) ? "" : $"&next_page_token={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendClassifiedAsync(request, "list meetings", ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var meeting in doc.RootElement.GetProperty("meetings").EnumerateArray())
            {
                var id = meeting.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
                if (meeting.TryGetProperty("agenda", out var agenda) && agenda.ValueKind == JsonValueKind.String)
                {
                    if (agenda.GetString() == correlationToken)
                        return (await GetMeetingAsync(accessToken, id, ct)).Meeting;
                    continue;
                }

                // The list didn't carry the agenda: only same-day meetings can be ours, so only fetch those.
                var start = meeting.TryGetProperty("start_time", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetDateTimeOffset() : (DateTimeOffset?)null;
                if (start is null || start.Value.UtcDateTime.Date != eventStart.UtcDateTime.Date)
                    continue;
                var details = await GetMeetingAsync(accessToken, id, ct);
                if (details.Agenda == correlationToken)
                    return details.Meeting;
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
                return null;
        }
    }

    private async Task<(string? Agenda, ZoomMeetingDto Meeting)> GetMeetingAsync(string accessToken, string meetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.zoom.us/v2/meetings/{meetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendClassifiedAsync(request, "get meeting", ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var agenda = root.TryGetProperty("agenda", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        var passcode = root.TryGetProperty("password", out var p) ? p.GetString() : null;
        return (agenda, new ZoomMeetingDto(
            root.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            root.GetProperty("join_url").GetString()!, OrganizerJoinUrl: null, passcode));
    }

    private async Task<HttpResponseMessage> SendClassifiedAsync(HttpRequestMessage request, string operation, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom {operation} failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Zoom {operation} timed out.", innerException: ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            await LogErrorBodyAsync(response, operation, ct);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode), $"Zoom {operation} returned {(int)response.StatusCode}.",
                RetryAfterOf(response));
        }
    }

    private async Task LogErrorBodyAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var errorBody = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Zoom {Operation} returned {StatusCode}: {ErrorBody}", operation, (int)response.StatusCode, errorBody);
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    /// <summary>Reads Zoom's numeric `code` field from an error response body, or null if the body
    /// isn't the expected JSON shape - never throws on a malformed/non-JSON body.</summary>
    private static async Task<int?> TryReadZoomErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
                ? code.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const int MaxAttendancePages = 100;

    public async Task<IReadOnlyList<ZoomAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Zoom's past-meeting participants report, paged by next_page_token (empty on the last page).
        var result = new List<ZoomAttendanceRecordDto>();
        string? pageToken = null;
        for (var page = 1; ; page++)
        {
            if (page > MaxAttendancePages)
                throw new InvalidOperationException($"Zoom participants report exceeded {MaxAttendancePages} pages.");

            var url = $"https://api.zoom.us/v2/past_meetings/{externalMeetingId}/participants?page_size=300"
                + (string.IsNullOrEmpty(pageToken) ? "" : $"&next_page_token={Uri.EscapeDataString(pageToken)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, ct);
            await EnsureSuccessOrLogAsync(response, "list past meeting participants", ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var participant in doc.RootElement.GetProperty("participants").EnumerateArray())
            {
                var name = participant.TryGetProperty("name", out var n) ? n.GetString() : null;
                var email = participant.TryGetProperty("user_email", out var e) ? e.GetString() : null;
                var joined = participant.GetProperty("join_time").GetDateTimeOffset();
                var left = participant.TryGetProperty("leave_time", out var l) && l.ValueKind != JsonValueKind.Null
                    ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
                result.Add(new ZoomAttendanceRecordDto(name, email, joined, left));
            }

            pageToken = doc.RootElement.TryGetProperty("next_page_token", out var token) ? token.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
                return result;
        }
    }

    /// <summary>Same reasoning as MicrosoftGraphMeetingClient.EnsureSuccessOrLogAsync — logs
    /// Zoom's actual error body (invalid field, missing scope, licensing issue) before throwing,
    /// since EnsureSuccessStatusCode() alone discards it. <paramref name="throwOnFailure"/> is false
    /// only when the caller is about to throw its own typed exception instead (CancelMeetingAsync's
    /// code-3002 case) - the body is still logged either way.</summary>
    private async Task EnsureSuccessOrLogAsync(
        HttpResponseMessage response, string operation, CancellationToken ct, bool throwOnFailure = true)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Zoom {Operation} returned {StatusCode}: {ErrorBody}",
                operation, (int)response.StatusCode, errorBody);
        }
        if (throwOnFailure)
            response.EnsureSuccessStatusCode();
    }
}
