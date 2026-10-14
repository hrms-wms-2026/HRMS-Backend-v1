using System.Net;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class MicrosoftGraphMeetingClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task CreateOrGetMeetingAsync_PostsToCreateOrGetWithTheExternalId_ReturnsJoinInfo()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/createOrGet", request.RequestUri!.ToString());
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { id = "graph-meeting-1", joinWebUrl = "https://teams.microsoft.com/l/meetup-join/abc" })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.CreateOrGetMeetingAsync(
            "access-token", "evt-1:2", "Sprint planning",
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal("graph-meeting-1", result.ExternalMeetingId);
        Assert.Contains("\"externalId\":\"evt-1:2\"", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MeetingProviderFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, MeetingProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, MeetingProviderFailureKind.Permanent)]
    [InlineData(HttpStatusCode.BadRequest, MeetingProviderFailureKind.Permanent)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MeetingProviderFailureKind.Ambiguous)]
    public async Task CreateOrGetMeetingAsync_ClassifiesFailures(HttpStatusCode status, MeetingProviderFailureKind kind)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("{}") };
            if (status == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => client.CreateOrGetMeetingAsync(
            "t", "e:1", "s", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        if (kind == MeetingProviderFailureKind.RateLimited)
            Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Fact]
    public async Task CreateOrGetMeetingAsync_ConnectionFailure_IsAmbiguous()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => client.CreateOrGetMeetingAsync(
            "t", "e:1", "s", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.Equal(MeetingProviderFailureKind.Ambiguous, ex.Kind);
    }

    [Fact]
    public async Task CancelMeetingAsync_AlreadyGone_IsSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "graph-meeting-1", CancellationToken.None);
    }

    [Fact]
    public async Task CancelMeetingAsync_SendsDeleteToTheMeetingsExternalId()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        await client.CancelMeetingAsync("access-token", "graph-meeting-1", CancellationToken.None);
    }

    [Fact]
    public async Task GetAttendanceAsync_NoReports_SaysNoReportExists()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = Array.Empty<object>() }) };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.False(result.ReportExists);
        Assert.Empty(result.Records);
    }

    [Fact]
    public async Task GetAttendanceAsync_HasReport_ReturnsFlattenedIntervalsFromMostRecentReport()
    {
        var callCount = 0;
        var handler = new StubHandler(request =>
        {
            callCount++;
            if (request.RequestUri!.ToString().EndsWith("attendanceReports"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { value = new[] { new { id = "report-1" } } })
                };
            }

            Assert.Equal(
                "https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports/report-1/attendanceRecords",
                request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    value = new[]
                    {
                        new
                        {
                            identity = new { displayName = "Ada Lovelace", upn = "ada@acme.com" },
                            attendanceIntervals = new[]
                            {
                                new { joinDateTime = "2026-09-23T10:00:00Z", leaveDateTime = "2026-09-23T10:30:00Z" }
                            }
                        }
                    }
                })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.True(result.ReportExists);
        var record = Assert.Single(result.Records);
        Assert.Equal("Ada Lovelace", record.ParticipantName);
        Assert.Equal("ada@acme.com", record.ParticipantEmail);
        Assert.NotNull(record.LeftAt);
    }

    [Fact]
    public async Task GetAttendanceAsync_ReportWithZeroRecords_IsAReportThatExists()
    {
        var handler = new StubHandler(request => request.RequestUri!.ToString().EndsWith("attendanceReports")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { new { id = "report-1" } } }) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = Array.Empty<object>() }) });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.True(result.ReportExists);
        Assert.Empty(result.Records);
    }

    [Fact]
    public async Task GetAttendanceAsync_RecordsOnSeveralPages_FollowsNextLinkAndReturnsThemAll()
    {
        const string page2 = "https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports/report-1/attendanceRecords?$skiptoken=abc";
        object Record(string name) => new
        {
            identity = new { displayName = name, upn = $"{name}@acme.com" },
            attendanceIntervals = new[] { new { joinDateTime = "2026-09-23T10:00:00Z", leaveDateTime = "2026-09-23T10:30:00Z" } }
        };
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("attendanceReports"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { new { id = "report-1" } } }) };
            if (url == page2)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = new[] { Record("grace") } }) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"value":[{"identity":{"displayName":"ada","upn":"ada@acme.com"},"attendanceIntervals":[{"joinDateTime":"2026-09-23T10:00:00Z","leaveDateTime":"2026-09-23T10:30:00Z"}]}],"@odata.nextLink":"{{page2}}"}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", CancellationToken.None);

        Assert.Equal(new[] { "ada", "grace" }, result.Records.Select(r => r.ParticipantName));
    }
}
