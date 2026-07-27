using FluentAssertions;
using System.Text.Json;
using ONEVO.Application.Features.AgentGateway.DTOs;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class AgentEventEnvelopeTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_CanonicalJson_MapsAllFields()
    {
        var eventId = Guid.NewGuid();
        var presenceSessionId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

        var json = $$"""
            {
                "event_id": "{{eventId}}",
                "type": "activity_snapshot",
                "schema_version": "1",
                "captured_at": "{{capturedAt:O}}",
                "presence_session_id": "{{presenceSessionId}}",
                "task_id": null,
                "data": { "keyboard_events_count": 4 }
            }
            """;

        var envelope = JsonSerializer.Deserialize<AgentEventEnvelope>(json, JsonOpts);

        envelope.Should().NotBeNull();
        envelope!.EventId.Should().Be(eventId);
        envelope.Type.Should().Be("activity_snapshot");
        envelope.SchemaVersion.Should().Be("1");
        envelope.CapturedAt.Should().Be(capturedAt);
        envelope.PresenceSessionId.Should().Be(presenceSessionId);
        envelope.TaskId.Should().BeNull();
        envelope.Data.GetProperty("keyboard_events_count").GetInt32().Should().Be(4);
    }

    [Fact]
    public void Deserialize_WithTaskId_MapsTaskId()
    {
        var taskId = Guid.NewGuid();
        var json = $$"""
            {
                "event_id": "{{Guid.NewGuid()}}",
                "type": "app_usage",
                "schema_version": "1",
                "captured_at": "2026-07-26T10:00:00Z",
                "presence_session_id": "{{Guid.NewGuid()}}",
                "task_id": "{{taskId}}",
                "data": {}
            }
            """;

        var envelope = JsonSerializer.Deserialize<AgentEventEnvelope>(json, JsonOpts);

        envelope!.TaskId.Should().Be(taskId);
    }

    [Fact]
    public void Deserialize_MeetingAppUsageType_MapsType()
    {
        var json = $$"""
            {
                "event_id": "{{Guid.NewGuid()}}",
                "type": "meeting_app_usage",
                "schema_version": "1",
                "captured_at": "2026-07-26T10:00:00Z",
                "presence_session_id": "{{Guid.NewGuid()}}",
                "task_id": null,
                "data": { "process_name": "teams.exe", "duration_seconds": 3600 }
            }
            """;

        var envelope = JsonSerializer.Deserialize<AgentEventEnvelope>(json, JsonOpts);

        envelope!.Type.Should().Be("meeting_app_usage");
    }
}
