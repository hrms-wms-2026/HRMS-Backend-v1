using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphMeetingClient(HttpClient httpClient, ILogger<MicrosoftGraphMeetingClient> logger) : ITeamsMeetingClient
{
    public async Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct,
        IReadOnlyList<string>? attendeeEmails = null)
    {
        var attendees = (attendeeEmails ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onlineMeetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            startDateTime = start.UtcDateTime.ToString("o"),
            endDateTime = end.UtcDateTime.ToString("o"),
            subject,
            participants = attendees.Count == 0 ? null : new { attendees = attendees.Select(e => new { upn = e, role = "attendee" }).ToArray() }
        }, options: new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "create online meeting", ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
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
        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessOrLogAsync(response, "cancel online meeting", ct);
    }

    private const int MaxPages = 100;

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

    /// <summary>Every page of a Graph collection, following <c>@odata.nextLink</c>.</summary>
    private async Task<List<JsonElement>> GetAllPagesAsync(string accessToken, string firstUrl, string operation, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        string? url = firstUrl;
        for (var page = 1; url is not null; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException($"Microsoft Graph {operation} exceeded {MaxPages} pages.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, ct);
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
