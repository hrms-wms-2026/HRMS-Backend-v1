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
    public async Task GetAttendanceAsync_NoReports_ReturnsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://graph.microsoft.com/v1.0/me/onlineMeetings/graph-meeting-1/attendanceReports", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = Array.Empty<object>() }) };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", SessionStart, SessionEnd, CancellationToken.None);

        Assert.Empty(result);
    }

    private static readonly DateTimeOffset SessionStart = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SessionEnd = SessionStart.AddMinutes(30);

    private static object Record(string name, string email, string joined, string left) => new
    {
        identity = new { displayName = name, upn = email },
        attendanceIntervals = new[] { new { joinDateTime = joined, leaveDateTime = left } }
    };

    [Fact]
    public async Task GetAttendanceAsync_TakesTheReportOfTheScheduledSession_NotTheLatestOne()
    {
        var requested = new List<string>();
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            if (url.EndsWith("attendanceReports"))
            {
                // A reused link: the session asked about, then a later one a week on (the newest, listed last).
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        value = new[]
                        {
                            new { id = "this-session", meetingStartDateTime = "2026-09-23T10:02:00Z" },
                            new { id = "next-week", meetingStartDateTime = "2026-09-30T10:01:00Z" },
                        }
                    })
                };
            }
            Assert.EndsWith("attendanceReports/this-session/attendanceRecords", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { value = new[] { Record("Ada Lovelace", "ada@acme.com", "2026-09-23T10:00:00Z", "2026-09-23T10:30:00Z") } })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", SessionStart, SessionEnd, CancellationToken.None);

        var record = Assert.Single(result);
        Assert.Equal("Ada Lovelace", record.ParticipantName);
        Assert.Equal("ada@acme.com", record.ParticipantEmail);
        Assert.NotNull(record.LeftAt);
        Assert.DoesNotContain(requested, u => u.Contains("next-week"));
    }

    [Fact]
    public async Task GetAttendanceAsync_NoReportForTheScheduledSession_ReturnsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            Assert.EndsWith("attendanceReports", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { value = new[] { new { id = "next-week", meetingStartDateTime = "2026-09-30T10:01:00Z" } } })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", SessionStart, SessionEnd, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAttendanceAsync_ReadsEveryPageOfReportsAndRecords()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("attendanceReports"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new Dictionary<string, object>
                    {
                        ["value"] = new[] { new { id = "older", meetingStartDateTime = "2026-09-16T10:00:00Z" } },
                        ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/reports-page-2",
                    })
                };
            if (url.EndsWith("reports-page-2"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { value = new[] { new { id = "this-session", meetingStartDateTime = "2026-09-23T10:00:00Z" } } })
                };
            if (url.EndsWith("this-session/attendanceRecords"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new Dictionary<string, object>
                    {
                        ["value"] = new[] { Record("Ada", "ada@acme.com", "2026-09-23T10:00:00Z", "2026-09-23T10:30:00Z") },
                        ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/records-page-2",
                    })
                };
            Assert.EndsWith("records-page-2", url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { value = new[] { Record("Grace", "grace@acme.com", "2026-09-23T10:05:00Z", "2026-09-23T10:30:00Z") } })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        var result = await client.GetAttendanceAsync("access-token", "graph-meeting-1", SessionStart, SessionEnd, CancellationToken.None);

        Assert.Equal(new[] { "ada@acme.com", "grace@acme.com" }, result.Select(r => r.ParticipantEmail));
    }

    [Fact]
    public async Task CreateMeetingAsync_AddsTheInvitedPeopleAsAttendees()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { id = "graph-meeting-1", joinWebUrl = "https://teams.microsoft.com/l/meetup-join/abc" })
            };
        });
        var client = new MicrosoftGraphMeetingClient(new HttpClient(handler), NullLogger<MicrosoftGraphMeetingClient>.Instance);

        await client.CreateMeetingAsync("access-token", "Sprint planning", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            CancellationToken.None, ["ada@acme.com", "ADA@acme.com", "vendor@example.com"]);

        using var doc = System.Text.Json.JsonDocument.Parse(body!);
        var attendees = doc.RootElement.GetProperty("participants").GetProperty("attendees").EnumerateArray()
            .Select(a => (a.GetProperty("upn").GetString(), a.GetProperty("role").GetString())).ToList();
        Assert.Equal(new[] { ("ada@acme.com", "attendee"), ("vendor@example.com", "attendee") }, attendees.Select(a => (a.Item1!, a.Item2!)));
    }
}
