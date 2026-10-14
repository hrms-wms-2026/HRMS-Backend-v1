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
            Assert.Contains("\"agenda\":\"onexso:evt:1\"", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
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
            "access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "onexso:evt:1", CancellationToken.None);

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

        await client.CreateMeetingAsync("access-token", "Multi-day offsite", start, end, "onexso:evt:1", CancellationToken.None);

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
    public async Task CancelMeetingAsync_OtherBadRequest_IsPermanent()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { code = 1001, message = "Meeting does not exist." })
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(
            () => client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None));

        Assert.Equal(MeetingProviderFailureKind.Permanent, ex.Kind);
    }

    [Fact]
    public async Task CancelMeetingAsync_NonJsonServerError_IsAmbiguous()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>upstream error</html>")
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(
            () => client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None));

        Assert.Equal(MeetingProviderFailureKind.Ambiguous, ex.Kind);
    }

    [Fact]
    public async Task GetAttendanceAsync_ReturnsFlattenedParticipants()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(
                "https://api.zoom.us/v2/past_meetings/987654321/participants?page_size=300",
                request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    participants = new[]
                    {
                        new
                        {
                            name = "Ada Lovelace", user_email = "ada@acme.com",
                            join_time = "2026-09-23T10:00:00Z", leave_time = "2026-09-23T10:30:00Z"
                        }
                    }
                })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", CancellationToken.None);

        var record = Assert.Single(result);
        Assert.Equal("Ada Lovelace", record.ParticipantName);
        Assert.Equal("ada@acme.com", record.ParticipantEmail);
        Assert.NotNull(record.LeftAt);
    }

    [Fact]
    public async Task GetAttendanceAsync_SeveralPages_FollowsNextPageTokenAndReturnsThemAll()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            object Participant(string name) => new { name, user_email = $"{name}@acme.com", join_time = "2026-09-23T10:00:00Z", leave_time = "2026-09-23T10:30:00Z" };
            return url.EndsWith("next_page_token=tok2")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { participants = new[] { Participant("grace") }, next_page_token = "" }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { participants = new[] { Participant("ada") }, next_page_token = "tok2" }) };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "987654321", CancellationToken.None);

        Assert.Equal(new[] { "ada", "grace" }, result.Select(r => r.ParticipantName));
    }

    [Fact]
    public async Task CancelMeetingAsync_AlreadyGone_IsSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"code":3001}""") });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "987654321", CancellationToken.None);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_MatchOnALaterPage_ReturnsItsDetails()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url == "https://api.zoom.us/v2/users/me/meetings?type=scheduled&page_size=300")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new[] { new { id = 111L, agenda = "something else", start_time = "2026-10-20T09:00:00Z" } },
                        next_page_token = "tok2"
                    })
                };
            if (url.EndsWith("next_page_token=tok2"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new[] { new { id = 222L, agenda = "onexso:evt:1", start_time = "2026-10-20T09:00:00Z" } },
                        next_page_token = ""
                    })
                };
            Assert.Equal("https://api.zoom.us/v2/meetings/222", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = 222L, agenda = "onexso:evt:1", join_url = "https://zoom.us/j/222", password = "abc" })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var match = await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("222", match!.ExternalMeetingId);
        Assert.Equal("https://zoom.us/j/222", match.JoinUrl);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_NoAgendaInTheList_FetchesDetailsOnlyForSameDayMeetings()
    {
        var detailCalls = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.zoom.us/v2/users/me/meetings"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        meetings = new object[]
                        {
                            new { id = 333L, start_time = "2026-10-21T09:00:00Z" },
                            new { id = 444L, start_time = "2026-10-20T15:00:00Z" }
                        },
                        next_page_token = ""
                    })
                };
            detailCalls.Add(url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = 444L, agenda = "onexso:evt:1", join_url = "https://zoom.us/j/444" })
            };
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        var match = await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal("444", match!.ExternalMeetingId);
        Assert.Equal(new[] { "https://api.zoom.us/v2/meetings/444" }, detailCalls);
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_CompleteListWithNoMatch_ReturnsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { meetings = new[] { new { id = 1L, agenda = "onexso:other:1", start_time = "2026-10-20T09:00:00Z" } }, next_page_token = "" })
        });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        Assert.Null(await client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task FindMeetingByCorrelationTokenAsync_ListForbidden_Throws()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
        var client = new ZoomMeetingClient(new HttpClient(handler), NullLogger<ZoomMeetingClient>.Instance);

        await Assert.ThrowsAsync<MeetingProviderException>(() =>
            client.FindMeetingByCorrelationTokenAsync("t", "onexso:evt:1", DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
