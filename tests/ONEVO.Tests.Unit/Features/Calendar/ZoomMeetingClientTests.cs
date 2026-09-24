using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Equal("https://us05web.zoom.us/s/987654321?zak=xyz", result.OrganizerJoinUrl);
        Assert.Equal("123456", result.PasscodeOrPin);
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
    public async Task GetAttendanceAsync_ReturnsFlattenedParticipants()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(
                "https://api.zoom.us/v2/past_meetings/987654321/participants",
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
}
