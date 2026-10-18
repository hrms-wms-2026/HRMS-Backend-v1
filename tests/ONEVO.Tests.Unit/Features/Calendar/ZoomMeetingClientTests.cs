using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class ZoomMeetingClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task CreateMeetingAsync_PostsToMeetingsEndpoint_ReturnsJoinInfo()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.zoom.us/v2/users/me/meetings", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                // start_url is a JWT (ZAK token): a host credential that both overflows the
                // organizer_join_url varchar(500) column and would be stored unencrypted if kept.
                // Even when Zoom's response includes it, the client must never read/return it.
                Content = JsonContent.Create(new
                {
                    id = 987654321L,
                    join_url = "https://us05web.zoom.us/j/987654321?pwd=abc",
                    start_url = "https://us05web.zoom.us/s/987654321?zak=xyz",
                    password = "123456"
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.CreateMeetingAsync(
            "access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        Assert.Equal("987654321", result.ExternalMeetingId);
        Assert.Equal("https://us05web.zoom.us/j/987654321?pwd=abc", result.JoinUrl);
        Assert.Null(result.OrganizerJoinUrl);
        Assert.Equal("123456", result.PasscodeOrPin);
    }

    [Fact]
    public async Task CreateMeetingAsync_DurationExceeds24Hours_ClampsTo1440Minutes()
    {
        // Zoom rejects scheduled meetings longer than 1440 minutes (24 hours) with a 400 - a
        // multi-day calendar event must not turn that into an uncaught HttpRequestException.
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    id = 987654321L,
                    join_url = "https://us05web.zoom.us/j/987654321",
                    password = "123456"
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);
        var start = DateTimeOffset.UtcNow;
        var end = start.AddDays(3); // way beyond Zoom's 1440-minute cap

        await client.CreateMeetingAsync("access-token", "Multi-day offsite", start, end, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains("\"duration\":1440", capturedBody);
    }

    [Fact]
    public async Task CancelMeetingAsync_SendsDeleteToTheMeetingsExternalId()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("https://api.zoom.us/v2/meetings/987654321", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None);
    }

    [Fact]
    public async Task CancelMeetingAsync_MeetingInProgress_ThrowsZoomMeetingInProgressException()
    {
        // Zoom rejects deleting a meeting that has already started (error code 3002) - this must
        // surface as a typed exception the command handler can catch, not bubble up as a generic
        // HttpRequestException that the API turns into an opaque 500.
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { code = 3002, message = "Meeting is in progress." })
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await Assert.ThrowsAsync<ZoomMeetingInProgressException>(
            () => client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None));
    }

    [Fact]
    public async Task CancelMeetingAsync_OtherBadRequest_ThrowsGenericHttpRequestException()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { code = 1001, message = "Meeting does not exist." })
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var ex = await Record.ExceptionAsync(
            () => client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex);
    }

    [Fact]
    public async Task CancelMeetingAsync_NonJsonErrorBody_ThrowsGenericHttpRequestException()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>upstream error</html>")
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var ex = await Record.ExceptionAsync(
            () => client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex);
    }

    private static readonly DateTimeOffset SessionStart = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SessionEnd = SessionStart.AddMinutes(30);

    private static object Participant(string name, string email, string joined, string left)
        => new { name, user_email = email, join_time = joined, leave_time = left };

    [Fact]
    public async Task GetAttendanceAsync_ReadsTheInstanceOfTheScheduledSession_NotTheLatestOne()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == "https://api.zoom.us/v2/past_meetings/987654321/instances")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new[]
                        {
                            new { uuid = "/abc//def==", start_time = "2026-09-23T10:01:00Z" },
                            new { uuid = "later==", start_time = "2026-09-30T10:00:00Z" },
                        }
                    })
                };
            }
            // A UUID that starts with '/' or contains '//' must be double-encoded (Zoom API rule).
            Assert.Equal("https://api.zoom.us/v2/past_meetings/%252Fabc%252F%252Fdef%253D%253D/participants?page_size=300", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { participants = new[] { Participant("Ada Lovelace", "ada@acme.com", "2026-09-23T10:00:00Z", "2026-09-23T10:30:00Z") } })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", SessionStart, SessionEnd, CancellationToken.None);

        var record = Assert.Single(result);
        Assert.Equal("Ada Lovelace", record.ParticipantName);
        Assert.Equal("ada@acme.com", record.ParticipantEmail);
        Assert.NotNull(record.LeftAt);
    }

    [Fact]
    public async Task GetAttendanceAsync_NoInstanceForTheScheduledSession_ReturnsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            Assert.EndsWith("/instances", request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { meetings = new[] { new { uuid = "later==", start_time = "2026-09-30T10:00:00Z" } } })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", SessionStart, SessionEnd, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAttendanceAsync_FollowsEveryParticipantsPage()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/instances"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { meetings = new[] { new { uuid = "plain==", start_time = "2026-09-23T10:00:00Z" } } })
                };
            if (!url.Contains("next_page_token"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        participants = new[] { Participant("Ada", "ada@acme.com", "2026-09-23T10:00:00Z", "2026-09-23T10:30:00Z") },
                        next_page_token = "page-2",
                    })
                };
            Assert.EndsWith("next_page_token=page-2", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    participants = new[] { Participant("Grace", "grace@acme.com", "2026-09-23T10:05:00Z", "2026-09-23T10:30:00Z") },
                    next_page_token = "",
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", SessionStart, SessionEnd, CancellationToken.None);

        Assert.Equal(new[] { "ada@acme.com", "grace@acme.com" }, result.Select(r => r.ParticipantEmail));
    }

    [Fact]
    public async Task CreateMeetingAsync_AddsTheInvitedPeopleAsMeetingInvitees()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { id = 987654321L, join_url = "https://us05web.zoom.us/j/987654321" })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await client.CreateMeetingAsync("access-token", "Sync", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None, ["ada@acme.com", "vendor@example.com"]);

        using var doc = System.Text.Json.JsonDocument.Parse(body!);
        var invitees = doc.RootElement.GetProperty("settings").GetProperty("meeting_invitees").EnumerateArray()
            .Select(i => i.GetProperty("email").GetString()).ToList();
        Assert.Equal(new[] { "ada@acme.com", "vendor@example.com" }, invitees);
    }
}
