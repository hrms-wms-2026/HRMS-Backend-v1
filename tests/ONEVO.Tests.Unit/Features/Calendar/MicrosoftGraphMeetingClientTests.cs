using System.Net;
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
    public async Task CreateMeetingAsync_PostsToOnlineMeetingsEndpoint_ReturnsJoinInfo()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new
                {
                    id = "graph-meeting-1",
                    joinWebUrl = "https://teams.microsoft.com/l/meetup-join/abc",
                    joinInformation = new { content = "Passcode: 123456" }
                })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.CreateMeetingAsync(
            "access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        Assert.Equal("graph-meeting-1", result.ExternalMeetingId);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", result.JoinUrl);
        Assert.Equal("Passcode: 123456", result.PasscodeOrPin);
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
