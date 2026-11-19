using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphMeetingClient(HttpClient httpClient, ILogger<MicrosoftGraphMeetingClient> logger) : ITeamsMeetingClient, IMeetingAttendanceSessionClient
{
    public async Task<TeamsMeetingDto> CreateOrGetMeetingAsync(
        string accessToken, string externalId, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct,
        IReadOnlyList<string>? attendeeEmails = null)
    {
        var attendees = (attendeeEmails ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onlineMeetings/createOrGet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            externalId,
            startDateTime = start.UtcDateTime.ToString("o"),
            endDateTime = end.UtcDateTime.ToString("o"),
            subject,
            participants = attendees.Count == 0 ? null : new { attendees = attendees.Select(e => new { upn = e, role = "attendee" }).ToArray() }
        }, options: new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        using var response = await SendClassifiedAsync(request, "create or get online meeting", ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var externalMeetingId = root.GetProperty("id").GetString()!;
        var joinUrl = root.GetProperty("joinWebUrl").GetString()!;
        string? passcode = null;
        if (root.TryGetProperty("joinInformation", out var joinInfo)
            && joinInfo.ValueKind != JsonValueKind.Null
            && joinInfo.TryGetProperty("content", out var content))
        {
            passcode = content.GetString();
        }

        return new TeamsMeetingDto(externalMeetingId, joinUrl, OrganizerJoinUrl: null, passcode);
    }

    public async Task CancelMeetingAsync(string accessToken, string externalMeetingId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // 404: already deleted - the goal state, not a failure.
        using var _ = await SendClassifiedAsync(request, "cancel online meeting", ct, HttpStatusCode.NotFound);
    }

    /// <summary>Sends and returns the response for 2xx (or an accepted status), otherwise logs Graph's
    /// error body and throws a MeetingProviderException classified for the caller's retry decision.</summary>
    private async Task<HttpResponseMessage> SendClassifiedAsync(
        HttpRequestMessage request, string operation, CancellationToken ct, params HttpStatusCode[] acceptedStatuses)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Microsoft Graph {operation} failed: {ex.Message}", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MeetingProviderException(MeetingProviderFailureKind.Ambiguous, $"Microsoft Graph {operation} timed out.", innerException: ex);
        }

        if (response.IsSuccessStatusCode || acceptedStatuses.Contains(response.StatusCode))
            return response;

        using (response)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Microsoft Graph {Operation} returned {StatusCode}: {ErrorBody}", operation, (int)response.StatusCode, errorBody);
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            throw new MeetingProviderException(
                MeetingProviderException.Classify(response.StatusCode),
                $"Microsoft Graph {operation} returned {(int)response.StatusCode}.", retryAfter);
        }
    }

    private const int MaxPages = 100;

    public string Provider => CalendarEventMeetingProviders.MicrosoftTeams;

    /// <summary>Every attendance report (one per held session) that started in [from, to], with its records' intervals -
    /// for per-occurrence sync of a link reused across a series (spec 5.8).</summary>
    public async Task<IReadOnlyList<ProviderAttendanceSession>> ListSessionsAsync(string accessToken, string externalMeetingId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var reports = await GetAllPagesAsync(
            accessToken, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports",
            "list attendance reports", ct, classified: true);
        var sessions = new List<ProviderAttendanceSession>();
        foreach (var report in reports)
        {
            if (DateOf(report, "meetingStartDateTime") is not { } start || start < from || start > to
                || StringOf(report, "id") is not { } reportId)
                continue;
            var records = await GetAllPagesAsync(
                accessToken,
                $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports/{reportId}/attendanceRecords",
                "list attendance records", ct, classified: true);
            var rows = new List<ProviderAttendanceRecord>();
            foreach (var record in records)
            {
                var identity = record.TryGetProperty("identity", out var i) && i.ValueKind == JsonValueKind.Object ? i : (JsonElement?)null;
                var name = identity is { } id1 ? StringOf(id1, "displayName") : null;
                var email = identity is { } id2 ? StringOf(id2, "upn") : null;
                var key = StringOf(record, "id") ?? (identity is { } id3 ? StringOf(id3, "id") : null) ?? email?.ToLowerInvariant() ?? name;
                if (key is null || !record.TryGetProperty("attendanceIntervals", out var intervals) || intervals.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var interval in intervals.EnumerateArray())
                    if (DateOf(interval, "joinDateTime") is { } joined)
                        rows.Add(new ProviderAttendanceRecord(key, name, email, joined, DateOf(interval, "leaveDateTime")));
            }
            sessions.Add(new ProviderAttendanceSession(reportId, start, DateOf(report, "meetingEndDateTime"), rows));
        }
        return sessions.OrderBy(s => s.Start).ToList();
    }

    private static string? StringOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : null;

    private static DateTimeOffset? DateOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetDateTimeOffset() : null;


    public async Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, DateTimeOffset sessionStart, DateTimeOffset sessionEnd, CancellationToken ct)
    {
        // Graph keeps one attendance report per session. A link reused across occurrences has several,
        // so take the report of the session asked about - never simply the newest (spec E6b).
        var reports = await GetAllPagesAsync(
            accessToken, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports",
            "list attendance reports", ct);
        var picked = AttendanceSessionMatcher.PickIndex(
            reports.Select(r => r.TryGetProperty("meetingStartDateTime", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetDateTimeOffset() : (DateTimeOffset?)null).ToList(),
            sessionStart, sessionEnd);
        if (picked is not { } index)
            return [];
        var reportId = reports[index].GetProperty("id").GetString();

        var records = await GetAllPagesAsync(
            accessToken,
            $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports/{reportId}/attendanceRecords",
            "list attendance records", ct);

        var result = new List<TeamsAttendanceRecordDto>();
        foreach (var record in records)
        {
            var identity = record.TryGetProperty("identity", out var i) ? i : default;
            var name = identity.ValueKind != JsonValueKind.Undefined && identity.TryGetProperty("displayName", out var n) ? n.GetString() : null;
            var email = identity.ValueKind != JsonValueKind.Undefined && identity.TryGetProperty("upn", out var e) ? e.GetString() : null;

            foreach (var interval in record.GetProperty("attendanceIntervals").EnumerateArray())
            {
                var joined = interval.GetProperty("joinDateTime").GetDateTimeOffset();
                var left = interval.TryGetProperty("leaveDateTime", out var l) && l.ValueKind != JsonValueKind.Null
                    ? l.GetDateTimeOffset() : (DateTimeOffset?)null;
                result.Add(new TeamsAttendanceRecordDto(name, email, joined, left));
            }
        }
        return result;
    }

    /// <summary>Every page of a Graph collection, following <c>@odata.nextLink</c>. <paramref name="classified"/> turns a
    /// failure into a classified MeetingProviderException (the attendance sync's retry decision) instead of an
    /// HttpRequestException.</summary>
    private async Task<List<JsonElement>> GetAllPagesAsync(string accessToken, string firstUrl, string operation, CancellationToken ct,
        bool classified = false)
    {
        var items = new List<JsonElement>();
        string? url = firstUrl;
        for (var page = 1; url is not null; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException($"Microsoft Graph {operation} exceeded {MaxPages} pages.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = classified ? await SendClassifiedAsync(request, operation, ct) : await httpClient.SendAsync(request, ct);
            if (!classified)
                await EnsureSuccessOrLogAsync(response, operation, ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            items.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(element => element.Clone()));
            url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        return items;
    }

    /// <summary>EnsureSuccessStatusCode() alone throws with no detail beyond the status code -
    /// Graph's actual error (invalid request field, missing permission, licensing issue) only
    /// ever shows up in the response body, which is otherwise lost. Logs it before throwing, same
    /// pattern as CalendarOAuthTokenExchangeClient.PostTokenRequestAsync.</summary>
    private async Task EnsureSuccessOrLogAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Microsoft Graph {Operation} returned {StatusCode}: {ErrorBody}",
                operation, (int)response.StatusCode, errorBody);
        }
        response.EnsureSuccessStatusCode();
    }
}
