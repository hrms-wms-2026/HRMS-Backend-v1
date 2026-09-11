using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class GoogleCalendarClient(HttpClient httpClient) : IGoogleCalendarClient
{
    private const string BaseUrl = "https://www.googleapis.com/calendar/v3";

    // Caps the number of pages a single ListEventsAsync call will follow. CalendarSyncService only
    // ever keeps the first BatchLimitPerConnection (200) events of what's accumulated here, so 25
    // pages (~5,000 events at maxResults=200/page) is already far more than one sync run needs. This
    // is a backstop against a malformed/looping provider response (e.g. a broken proxy echoing the
    // same nextPageToken forever) spinning this loop indefinitely inside one job tick.
    private const int MaxPages = 25;

    public async Task<GoogleCalendarPage> ListEventsAsync(string accessToken, string calendarId, string? syncToken, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        // Google only returns nextSyncToken on the FINAL page of a paginated response; every
        // non-final page returns nextPageToken instead. We must follow nextPageToken in a loop
        // until we reach the page carrying nextSyncToken, accumulating events across all pages,
        // otherwise a calendar with more than one page of events (>maxResults) never advances its
        // sync token and gets stuck re-fetching only the first page forever. Per Google's actual
        // API contract, syncToken is only sent on the very first request of a sync cycle - follow-up
        // page requests use pageToken instead (not both).
        var events = new List<GoogleCalendarEventDto>();
        string? nextSyncToken = null;
        string? pageToken = null;
        var pageCount = 0;
        var cappedOut = false;

        do
        {
            var url = BuildListUrl(calendarId, syncToken, pageToken, windowStart, windowEnd);

            using var response = await SendAsync(HttpMethod.Get, url, accessToken, body: null, ct);
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
                events.Add(ParseEvent(item));

            nextSyncToken = doc.RootElement.TryGetProperty("nextSyncToken", out var t) ? t.GetString() : null;
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var p) ? p.GetString() : null;
            pageCount++;

            if (pageToken is not null && pageCount >= MaxPages)
            {
                // We haven't actually reached the provider's final page - don't claim we have by
                // returning a sync token (which would permanently skip whatever pages remain).
                cappedOut = true;
                break;
            }
        } while (pageToken is not null);

        return new GoogleCalendarPage(events, cappedOut ? null : nextSyncToken);
    }

    private static string BuildListUrl(string calendarId, string? syncToken, string? pageToken, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var baseEventsUrl = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";

        if (pageToken is not null)
        {
            if (syncToken is not null)
                // Follow-up page of an incremental (syncToken-based) sync - only pageToken is sent,
                // matching Google's documented pagination contract (syncToken and pageToken must
                // never appear together on the same request).
                return $"{baseEventsUrl}?pageToken={Uri.EscapeDataString(pageToken)}&maxResults=200";

            // Follow-up page of a window-based (first) sync. Per Google's pagination contract, every
            // query parameter besides pageToken must stay identical across the paginated request
            // sequence - the server uses the originating request's parameters to continue the same
            // result set. Dropping timeMin/timeMax/singleEvents here would risk a 400, or worse, a
            // later page silently returning results outside the intended window or with un-expanded
            // recurring event masters.
            return $"{baseEventsUrl}?timeMin={Uri.EscapeDataString(windowStart.ToString("O"))}&timeMax={Uri.EscapeDataString(windowEnd.ToString("O"))}&maxResults=200&singleEvents=true&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        return syncToken is not null
            ? $"{baseEventsUrl}?syncToken={Uri.EscapeDataString(syncToken)}&maxResults=200"
            : $"{baseEventsUrl}?timeMin={Uri.EscapeDataString(windowStart.ToString("O"))}&timeMax={Uri.EscapeDataString(windowEnd.ToString("O"))}&maxResults=200&singleEvents=true";
    }

    public async Task<GoogleCalendarEventDto> InsertEventAsync(string accessToken, string calendarId, GoogleCalendarEventDto @event, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";
        using var response = await SendAsync(HttpMethod.Post, url, accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task<GoogleCalendarEventDto> PatchEventAsync(string accessToken, string calendarId, string eventId, GoogleCalendarEventDto @event, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        using var response = await SendAsync(HttpMethod.Patch, url, accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct)
    {
        var url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        using var response = await SendAsync(HttpMethod.Delete, url, accessToken, body: null, ct);
        response.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static string ToJsonBody(GoogleCalendarEventDto e)
    {
        var payload = new Dictionary<string, object?>
        {
            ["summary"] = e.Title,
            ["description"] = e.Description,
            ["location"] = e.Location,
            ["start"] = e.IsAllDay
                ? new Dictionary<string, object?> { ["date"] = e.Start.ToString("yyyy-MM-dd") }
                : new Dictionary<string, object?> { ["dateTime"] = e.Start.ToString("O"), ["timeZone"] = e.Timezone },
            ["end"] = e.IsAllDay
                ? new Dictionary<string, object?> { ["date"] = e.End.ToString("yyyy-MM-dd") }
                : new Dictionary<string, object?> { ["dateTime"] = e.End.ToString("O"), ["timeZone"] = e.Timezone }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static GoogleCalendarEventDto ParseEvent(JsonElement item)
    {
        var status = item.TryGetProperty("status", out var s) ? s.GetString() : null;

        if (status == "cancelled")
        {
            return new GoogleCalendarEventDto(
                Id: item.GetProperty("id").GetString()!,
                Etag: item.TryGetProperty("etag", out var cancelledEtag) ? cancelledEtag.GetString() : null,
                Title: string.Empty,
                Description: null,
                Start: DateTimeOffset.MinValue,
                End: DateTimeOffset.MinValue,
                IsAllDay: false,
                Timezone: null,
                Location: null,
                IsCancelled: true,
                IsPrivate: false);
        }

        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        var isAllDay = start.TryGetProperty("date", out _);

        DateTimeOffset ParseWhen(JsonElement whenElement) => isAllDay
            ? new DateTimeOffset(DateOnly.Parse(whenElement.GetProperty("date").GetString()!).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.Parse(whenElement.GetProperty("dateTime").GetString()!);

        return new GoogleCalendarEventDto(
            Id: item.GetProperty("id").GetString()!,
            Etag: item.TryGetProperty("etag", out var etag) ? etag.GetString() : null,
            Title: item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
            Description: item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Start: ParseWhen(start),
            End: ParseWhen(end),
            IsAllDay: isAllDay,
            Timezone: !isAllDay && start.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null,
            Location: item.TryGetProperty("location", out var loc) ? loc.GetString() : null,
            IsCancelled: status == "cancelled",
            IsPrivate: item.TryGetProperty("visibility", out var vis) && (vis.GetString() == "private" || vis.GetString() == "confidential"));
    }
}
