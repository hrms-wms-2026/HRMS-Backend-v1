using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphMeetingClient(HttpClient httpClient, ILogger<MicrosoftGraphMeetingClient> logger) : ITeamsMeetingClient
{
    public async Task<TeamsMeetingDto> CreateMeetingAsync(
        string accessToken, string subject, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/onlineMeetings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            startDateTime = start.UtcDateTime.ToString("o"),
            endDateTime = end.UtcDateTime.ToString("o"),
            subject
        });
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

    public async Task<TeamsAttendanceResult> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // One report per session; the last one is the most recent. Every list is paged by Graph.
        var reports = await GetAllPagesAsync(
            accessToken, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports",
            "list attendance reports", ct);
        if (reports.Count == 0)
            return new TeamsAttendanceResult(false, []);
        var reportId = reports[^1].GetProperty("id").GetString();

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
        return new TeamsAttendanceResult(true, result);
    }

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
