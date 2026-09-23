using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphMeetingClient(HttpClient httpClient) : ITeamsMeetingClient
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
        response.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TeamsAttendanceRecordDto>> GetAttendanceAsync(
        string accessToken, string externalMeetingId, CancellationToken ct)
    {
        // Graph returns a list of attendance *reports* per meeting (one per session, for a
        // recurring/reconvened meeting); Phase 1 events are non-recurring single sessions, so take
        // the most recent report only.
        using var reportsRequest = new HttpRequestMessage(
            HttpMethod.Get, $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports");
        reportsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var reportsResponse = await httpClient.SendAsync(reportsRequest, ct);
        reportsResponse.EnsureSuccessStatusCode();
        using var reportsStream = await reportsResponse.Content.ReadAsStreamAsync(ct);
        using var reportsDoc = await JsonDocument.ParseAsync(reportsStream, cancellationToken: ct);
        var reports = reportsDoc.RootElement.GetProperty("value").EnumerateArray().ToList();
        if (reports.Count == 0)
            return [];
        var reportId = reports[^1].GetProperty("id").GetString();

        using var recordsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/onlineMeetings/{externalMeetingId}/attendanceReports/{reportId}/attendanceRecords");
        recordsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var recordsResponse = await httpClient.SendAsync(recordsRequest, ct);
        recordsResponse.EnsureSuccessStatusCode();
        using var recordsStream = await recordsResponse.Content.ReadAsStreamAsync(ct);
        using var recordsDoc = await JsonDocument.ParseAsync(recordsStream, cancellationToken: ct);

        var result = new List<TeamsAttendanceRecordDto>();
        foreach (var record in recordsDoc.RootElement.GetProperty("value").EnumerateArray())
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
}
