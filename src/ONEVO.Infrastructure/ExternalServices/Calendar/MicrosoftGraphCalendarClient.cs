using System.Net.Http.Headers;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class MicrosoftGraphCalendarClient(HttpClient httpClient) : IMicrosoftGraphCalendarClient
{
    private const string BaseUrl = "https://graph.microsoft.com/v1.0";

    public async Task<GraphCalendarPage> ListEventsAsync(string accessToken, string? deltaLink, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var url = deltaLink ?? $"{BaseUrl}/me/calendarView/delta?startDateTime={Uri.EscapeDataString(windowStart.ToString("O"))}&endDateTime={Uri.EscapeDataString(windowEnd.ToString("O"))}";

        using var response = await SendAsync(HttpMethod.Get, url, accessToken, body: null, ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var events = new List<GraphEventDto>();
        foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            events.Add(ParseEvent(item));

        var nextDeltaLink = doc.RootElement.TryGetProperty("@odata.deltaLink", out var d) ? d.GetString() : null;
        return new GraphCalendarPage(events, nextDeltaLink);
    }

    public async Task<GraphEventDto> CreateEventAsync(string accessToken, GraphEventDto @event, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{BaseUrl}/me/events", accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task<GraphEventDto> UpdateEventAsync(string accessToken, string eventId, GraphEventDto @event, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"{BaseUrl}/me/events/{Uri.EscapeDataString(eventId)}", accessToken, ToJsonBody(@event), ct);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEvent(doc.RootElement);
    }

    public async Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"{BaseUrl}/me/events/{Uri.EscapeDataString(eventId)}", accessToken, body: null, ct);
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

    private static string ToJsonBody(GraphEventDto e)
    {
        var payload = new Dictionary<string, object?>
        {
            ["subject"] = e.Title,
            ["body"] = new Dictionary<string, object?> { ["contentType"] = "text", ["content"] = e.Description ?? string.Empty },
            ["isAllDay"] = e.IsAllDay,
            ["location"] = new Dictionary<string, object?> { ["displayName"] = e.Location },
            ["start"] = new Dictionary<string, object?> { ["dateTime"] = e.Start.UtcDateTime.ToString("s"), ["timeZone"] = "UTC" },
            ["end"] = new Dictionary<string, object?> { ["dateTime"] = e.End.UtcDateTime.ToString("s"), ["timeZone"] = "UTC" }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static GraphEventDto ParseEvent(JsonElement item)
    {
        if (item.TryGetProperty("@removed", out _))
        {
            return new GraphEventDto(
                Id: item.GetProperty("id").GetString()!,
                Etag: null,
                Title: string.Empty,
                Description: null,
                Start: DateTimeOffset.MinValue,
                End: DateTimeOffset.MinValue,
                IsAllDay: false,
                Timezone: null,
                Location: null,
                IsCancelled: true);
        }

        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        var timezone = start.TryGetProperty("timeZone", out var tz) ? tz.GetString() : null;

        return new GraphEventDto(
            Id: item.GetProperty("id").GetString()!,
            Etag: item.TryGetProperty("@odata.etag", out var etag) ? etag.GetString() : null,
            Title: item.TryGetProperty("subject", out var subject) ? subject.GetString() ?? string.Empty : string.Empty,
            Description: item.TryGetProperty("body", out var body) && body.TryGetProperty("content", out var content) ? content.GetString() : null,
            Start: DateTimeOffset.Parse(start.GetProperty("dateTime").GetString()!),
            End: DateTimeOffset.Parse(end.GetProperty("dateTime").GetString()!),
            IsAllDay: item.TryGetProperty("isAllDay", out var allDay) && allDay.GetBoolean(),
            Timezone: timezone,
            Location: item.TryGetProperty("location", out var loc) && loc.TryGetProperty("displayName", out var name) ? name.GetString() : null,
            IsCancelled: item.TryGetProperty("isCancelled", out var cancelled) && cancelled.GetBoolean());
    }
}
