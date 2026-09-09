using System.Net;
using System.Text;
using System.Text.Json;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GoogleCalendarClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ListEventsAsync_ParsesTimedAndAllDayEvents_AndCapturesNextSyncToken()
    {
        var handler = new StubHandler(_ => JsonResponse(new
        {
            items = new object[]
            {
                new
                {
                    id = "evt-timed",
                    etag = "\"etag-1\"",
                    summary = "Timed Event",
                    description = "A timed event",
                    start = new { dateTime = "2026-09-10T09:00:00+00:00", timeZone = "UTC" },
                    end = new { dateTime = "2026-09-10T10:00:00+00:00", timeZone = "UTC" },
                    location = "Room 1",
                    status = "confirmed"
                },
                new
                {
                    id = "evt-allday",
                    etag = "\"etag-2\"",
                    summary = "All Day Event",
                    description = (string?)null,
                    start = new { date = "2026-09-11" },
                    end = new { date = "2026-09-12" },
                    location = (string?)null,
                    status = "confirmed"
                }
            },
            nextSyncToken = "sync-token-123"
        }));
        var sut = new GoogleCalendarClient(new HttpClient(handler));

        var result = await sut.ListEventsAsync("at-1", "primary", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), CancellationToken.None);

        Assert.Equal("sync-token-123", result.NextSyncToken);
        Assert.Equal(2, result.Events.Count);

        var timed = result.Events[0];
        Assert.Equal("evt-timed", timed.Id);
        Assert.False(timed.IsAllDay);
        Assert.Equal("UTC", timed.Timezone);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), timed.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero), timed.End);
        Assert.False(timed.IsCancelled);

        var allDay = result.Events[1];
        Assert.Equal("evt-allday", allDay.Id);
        Assert.True(allDay.IsAllDay);
        Assert.Null(allDay.Timezone);
        Assert.Equal(DateTimeOffset.Parse("2026-09-11"), allDay.Start);
        Assert.Equal(DateTimeOffset.Parse("2026-09-12"), allDay.End);
    }

    [Fact]
    public async Task InsertEventAsync_PostsToEventsUrl_AndParsesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse(new
            {
                id = "evt-new",
                etag = "\"etag-new\"",
                summary = "New Event",
                description = "Created",
                start = new { dateTime = "2026-09-15T13:00:00+00:00", timeZone = "UTC" },
                end = new { dateTime = "2026-09-15T14:00:00+00:00", timeZone = "UTC" },
                location = "HQ",
                status = "confirmed"
            });
        });
        var sut = new GoogleCalendarClient(new HttpClient(handler));

        var toInsert = new GoogleCalendarEventDto(
            Id: string.Empty,
            Etag: null,
            Title: "New Event",
            Description: "Created",
            Start: new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.Zero),
            End: new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero),
            IsAllDay: false,
            Timezone: "UTC",
            Location: "HQ",
            IsCancelled: false);

        var result = await sut.InsertEventAsync("at-1", "primary", toInsert, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://www.googleapis.com/calendar/v3/calendars/primary/events", capturedRequest.RequestUri!.ToString());

        Assert.Equal("evt-new", result.Id);
        Assert.Equal("New Event", result.Title);
        Assert.Equal("HQ", result.Location);
        Assert.False(result.IsAllDay);
    }
}
