using System.Net;
using System.Text;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class MicrosoftGraphCalendarClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage RawJsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ListEventsAsync_FollowsODataNextLink_AccumulatesAllPagesAndUsesFinalPageDeltaLink()
    {
        const string page1Json = """
        {
          "value": [
            { "id": "evt-page1", "subject": "Page 1 Event", "body": { "contentType": "text", "content": null }, "isAllDay": false, "location": { "displayName": null }, "start": { "dateTime": "2026-09-10T09:00:00", "timeZone": "UTC" }, "end": { "dateTime": "2026-09-10T09:30:00", "timeZone": "UTC" }, "isCancelled": false }
          ],
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$skiptoken=page2token"
        }
        """;
        const string page2Json = """
        {
          "value": [
            { "id": "evt-page2", "subject": "Page 2 Event", "body": { "contentType": "text", "content": null }, "isAllDay": false, "location": { "displayName": null }, "start": { "dateTime": "2026-09-11T09:00:00", "timeZone": "UTC" }, "end": { "dateTime": "2026-09-11T09:30:00", "timeZone": "UTC" }, "isCancelled": false }
          ],
          "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=finaltoken"
        }
        """;

        var requestedUrls = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requestedUrls.Add(url);
            return RawJsonResponse(url.Contains("skiptoken=page2token") ? page2Json : page1Json);
        });
        var sut = new MicrosoftGraphCalendarClient(new HttpClient(handler));

        var result = await sut.ListEventsAsync("at-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal(2, requestedUrls.Count);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/calendarView/delta?$skiptoken=page2token", requestedUrls[1]);

        Assert.Equal(2, result.Events.Count);
        Assert.Contains(result.Events, e => e.Id == "evt-page1");
        Assert.Contains(result.Events, e => e.Id == "evt-page2");
        Assert.Equal("https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=finaltoken", result.NextDeltaLink);
    }

    [Fact]
    public async Task ListEventsAsync_ParsesEvents_AndCapturesNextDeltaLink()
    {
        const string responseJson = """
        {
          "value": [
            {
              "id": "evt-1",
              "subject": "Standup",
              "body": { "contentType": "text", "content": "Daily standup" },
              "isAllDay": false,
              "location": { "displayName": "Room A" },
              "start": { "dateTime": "2026-09-10T09:00:00", "timeZone": "UTC" },
              "end": { "dateTime": "2026-09-10T09:30:00", "timeZone": "UTC" },
              "isCancelled": false
            },
            {
              "id": "evt-2",
              "subject": "Company Holiday",
              "body": { "contentType": "text", "content": null },
              "isAllDay": true,
              "location": { "displayName": null },
              "start": { "dateTime": "2026-09-11T00:00:00", "timeZone": "UTC" },
              "end": { "dateTime": "2026-09-12T00:00:00", "timeZone": "UTC" },
              "isCancelled": false
            }
          ],
          "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=abc123"
        }
        """;
        var handler = new StubHandler(_ => RawJsonResponse(responseJson));
        var sut = new MicrosoftGraphCalendarClient(new HttpClient(handler));

        var result = await sut.ListEventsAsync("at-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal("https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=abc123", result.NextDeltaLink);
        Assert.Equal(2, result.Events.Count);

        var first = result.Events[0];
        Assert.Equal("evt-1", first.Id);
        Assert.Equal("Standup", first.Title);
        Assert.Equal("Daily standup", first.Description);
        Assert.False(first.IsAllDay);
        Assert.Equal("Room A", first.Location);
        Assert.Equal("UTC", first.Timezone);
        Assert.False(first.IsCancelled);

        var second = result.Events[1];
        Assert.Equal("evt-2", second.Id);
        Assert.True(second.IsAllDay);
        Assert.Null(second.Description);
        Assert.Null(second.Location);
    }

    [Fact]
    public async Task CreateEventAsync_PostsToEventsUrl_AndParsesResponse()
    {
        const string responseJson = """
        {
          "id": "evt-new",
          "@odata.etag": "W/\"etag-new\"",
          "subject": "New Meeting",
          "body": { "contentType": "text", "content": "Kickoff" },
          "isAllDay": false,
          "location": { "displayName": "HQ" },
          "start": { "dateTime": "2026-09-15T13:00:00", "timeZone": "UTC" },
          "end": { "dateTime": "2026-09-15T14:00:00", "timeZone": "UTC" },
          "isCancelled": false
        }
        """;
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return RawJsonResponse(responseJson);
        });
        var sut = new MicrosoftGraphCalendarClient(new HttpClient(handler));

        var toCreate = new GraphEventDto(
            Id: string.Empty,
            Etag: null,
            Title: "New Meeting",
            Description: "Kickoff",
            Start: new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Timezone: "UTC",
            Location: "HQ",
            IsCancelled: false,
            IsPrivate: false);

        var result = await sut.CreateEventAsync("at-1", toCreate, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/events", capturedRequest.RequestUri!.ToString());

        Assert.Equal("evt-new", result.Id);
        Assert.Equal("New Meeting", result.Title);
        Assert.Equal("HQ", result.Location);
        Assert.False(result.IsAllDay);
    }

    [Fact]
    public async Task ListEventsAsync_ParsesRemovedDeltaEvent_WithoutThrowing()
    {
        const string responseJson = """
        {
          "value": [
            {
              "id": "evt-removed",
              "@removed": { "reason": "deleted" }
            }
          ],
          "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=xyz789"
        }
        """;
        var handler = new StubHandler(_ => RawJsonResponse(responseJson));
        var sut = new MicrosoftGraphCalendarClient(new HttpClient(handler));

        var result = await sut.ListEventsAsync("at-1", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Single(result.Events);
        var removed = result.Events[0];
        Assert.Equal("evt-removed", removed.Id);
        Assert.True(removed.IsCancelled);
    }

    [Fact]
    public async Task CreateEventAsync_SendsInstantConvertedToUtc_RegardlessOfSuppliedTimezone()
    {
        const string responseJson = """
        {
          "id": "evt-new",
          "subject": "IST Meeting",
          "body": { "contentType": "text", "content": "Kickoff" },
          "isAllDay": false,
          "location": { "displayName": "HQ" },
          "start": { "dateTime": "2026-09-15T13:00:00", "timeZone": "UTC" },
          "end": { "dateTime": "2026-09-15T14:00:00", "timeZone": "UTC" },
          "isCancelled": false
        }
        """;
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return RawJsonResponse(responseJson);
        });
        var sut = new MicrosoftGraphCalendarClient(new HttpClient(handler));

        // IST offset (+05:30) deliberately paired with a mismatched Timezone label (Pacific Standard Time)
        // to prove the client converts the actual instant to UTC rather than trusting e.Timezone.
        var istOffset = TimeSpan.FromHours(5.5);
        var toCreate = new GraphEventDto(
            Id: string.Empty,
            Etag: null,
            Title: "IST Meeting",
            Description: "Kickoff",
            Start: new DateTimeOffset(2026, 9, 15, 18, 30, 0, istOffset),
            End: new DateTimeOffset(2026, 9, 15, 19, 30, 0, istOffset),
            IsAllDay: false,
            Timezone: "Pacific Standard Time",
            Location: "HQ",
            IsCancelled: false,
            IsPrivate: false);

        await sut.CreateEventAsync("at-1", toCreate, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;

        var expectedStartUtc = toCreate.Start.UtcDateTime.ToString("s");
        var expectedEndUtc = toCreate.End.UtcDateTime.ToString("s");

        Assert.Equal(expectedStartUtc, root.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("UTC", root.GetProperty("start").GetProperty("timeZone").GetString());
        Assert.Equal(expectedEndUtc, root.GetProperty("end").GetProperty("dateTime").GetString());
        Assert.Equal("UTC", root.GetProperty("end").GetProperty("timeZone").GetString());

        // Sanity: the UTC-converted wall-clock digits must differ from the raw IST wall-clock digits,
        // proving an actual conversion happened rather than a pass-through of e.Start's local digits.
        Assert.NotEqual(toCreate.Start.ToString("s"), root.GetProperty("start").GetProperty("dateTime").GetString());
    }
}
