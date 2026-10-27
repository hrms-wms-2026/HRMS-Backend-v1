using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>The /meetings contract the frontend builds against (Plan B Task 1, docs/api/meetings-v1.md).</summary>
public sealed class MeetingContractsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Records_RoundTripAsCamelCaseJson()
    {
        var create = new CreateMeetingRequest(Guid.NewGuid(), Guid.NewGuid(), "Standup", "Agenda", null, false,
            new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), 30, false, "Asia/Colombo", "FREQ=WEEKLY",
            [Guid.NewGuid()], ["guest@example.test"], "zoom");
        var occurrence = new MeetingOccurrenceResponse(Guid.NewGuid(), "ACME-M1", Guid.NewGuid(),
            create.Start, create.Start, create.Start.AddMinutes(30), false, "Asia/Colombo", "Standup", null, null, false,
            MeetingStatuses.Scheduled, false, 0, 0, "FREQ=WEEKLY",
            new MeetingPlacementView(Guid.NewGuid(), "Acme", Guid.NewGuid(), "Delivery", false),
            new MeetingPersonRef(Guid.NewGuid(), "Priya K."), "organizer", "accepted",
            [new MeetingAttendeeView("employee", Guid.NewGuid(), "Arjun M.", null, "pending", null, "series")], true);

        var createJson = JsonSerializer.Serialize(create, Web);
        var occurrenceJson = JsonSerializer.Serialize(occurrence, Web);

        Assert.Contains("\"participantEmployeeIds\"", createJson);
        Assert.Contains("\"taskId\"", occurrenceJson);
        Assert.Contains("\"originalStart\"", occurrenceJson);
        Assert.Contains("\"myRole\"", occurrenceJson);
        var back = JsonSerializer.Deserialize<CreateMeetingRequest>(createJson, Web)!;
        Assert.Equal(create.Title, back.Title);
        Assert.Equal(create.ParticipantEmployeeIds, back.ParticipantEmployeeIds);
        Assert.Equal(create.Start, back.Start);
    }

    [Fact]
    public void ErrorCodes_AreUniqueSnakeCase()
    {
        var codes = typeof(MeetingErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.True(codes.Count >= 20, $"only {codes.Count} codes");
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches(new Regex("^[a-z_]+$"), c));
        Assert.Contains("meeting_changed", codes);
        Assert.Contains("meetings_disabled", codes);
    }
}
