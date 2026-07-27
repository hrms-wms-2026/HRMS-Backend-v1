using FluentAssertions;
using System.Text.Json;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Infrastructure.Services.ActivityMonitoring;

namespace ONEVO.Tests.Unit.Features.ActivityMonitoring;

public sealed class ActivityEventParserTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static AgentEventEnvelope MakeEnvelope(string type, DateTimeOffset capturedAt, string dataJson) =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = type,
            SchemaVersion = "1",
            CapturedAt = capturedAt,
            PresenceSessionId = Guid.NewGuid(),
            Data = JsonDocument.Parse(dataJson).RootElement
        };

    // ── ParseActivitySnapshot ────────────────────────────────────────────────

    [Fact]
    public void ParseActivitySnapshot_UsesCapturedAtFromEnvelope_NotServerTime()
    {
        var capturedAt = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        var envelope = MakeEnvelope("activity_snapshot", capturedAt,
            """{"keyboard_events_count":100,"mouse_events_count":50,"active_seconds":60,"idle_seconds":0,"foreground_process_name":"code.exe"}""");

        var snapshot = ActivityEventParser.ParseActivitySnapshot(envelope, TenantId, EmployeeId);

        snapshot.CapturedAt.Should().Be(capturedAt);
        snapshot.TenantId.Should().Be(TenantId);
        snapshot.EmployeeId.Should().Be(EmployeeId);
        snapshot.KeyboardEventsCount.Should().Be(100);
        snapshot.MouseEventsCount.Should().Be(50);
        snapshot.ForegroundProcessName.Should().Be("code.exe");
    }

    [Fact]
    public void ParseActivitySnapshot_IntensityIsCappedAt100()
    {
        var envelope = MakeEnvelope("activity_snapshot", DateTimeOffset.UtcNow,
            """{"keyboard_events_count":5000,"mouse_events_count":5000,"active_seconds":60,"idle_seconds":0,"foreground_process_name":"code.exe"}""");

        var snapshot = ActivityEventParser.ParseActivitySnapshot(envelope, TenantId, EmployeeId);

        snapshot.IntensityScore.Should().Be(100);
    }

    // ── ParseMeetingAppUsage ─────────────────────────────────────────────────

    [Fact]
    public void ParseMeetingAppUsage_UsesStartAndEndFromPayload()
    {
        var start = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
        var envelope = MakeEnvelope("meeting_app_usage", end,
            $$"""{"process_name":"teams.exe","start":"{{start:O}}","end":"{{end:O}}","duration_seconds":3600,"camera_active":true,"microphone_active":false}""");

        var session = ActivityEventParser.ParseMeetingAppUsage(envelope, TenantId, EmployeeId);

        session.Should().NotBeNull();
        session!.MeetingStart.Should().Be(start);
        session.MeetingEnd.Should().Be(end);
        session.TenantId.Should().Be(TenantId);
        session.EmployeeId.Should().Be(EmployeeId);
    }

    [Fact]
    public void ParseMeetingAppUsage_CameraAndMicFlagsPreserved()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var end = DateTimeOffset.UtcNow;
        var envelope = MakeEnvelope("meeting_app_usage", end,
            $$"""{"process_name":"zoom.exe","start":"{{start:O}}","end":"{{end:O}}","duration_seconds":3600,"camera_active":true,"microphone_active":true}""");

        var session = ActivityEventParser.ParseMeetingAppUsage(envelope, TenantId, EmployeeId);

        session!.HadCameraOn.Should().BeTrue();
        session.HadMicActivity.Should().BeTrue();
    }

    [Fact]
    public void ParseMeetingAppUsage_PlatformStripsExeSuffix()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var end = DateTimeOffset.UtcNow;
        var envelope = MakeEnvelope("meeting_app_usage", end,
            $$"""{"process_name":"teams.exe","start":"{{start:O}}","end":"{{end:O}}","duration_seconds":3600,"camera_active":false,"microphone_active":false}""");

        var session = ActivityEventParser.ParseMeetingAppUsage(envelope, TenantId, EmployeeId);

        session!.Platform.Should().Be("teams");
    }

    [Fact]
    public void ParseMeetingAppUsage_DurationMinutesRoundedFromSeconds()
    {
        var start = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        var envelope = MakeEnvelope("meeting_app_usage", end,
            $$"""{"process_name":"teams.exe","start":"{{start:O}}","end":"{{end:O}}","duration_seconds":1800,"camera_active":false,"microphone_active":false}""");

        var session = ActivityEventParser.ParseMeetingAppUsage(envelope, TenantId, EmployeeId);

        session!.DurationMinutes.Should().Be(30);
    }

    // ── ParseAppUsage ────────────────────────────────────────────────────────

    [Fact]
    public void ParseAppUsage_UsesDateFromCapturedAt()
    {
        var capturedAt = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        var envelope = MakeEnvelope("app_usage", capturedAt,
            """{"process_name":"code.exe","application_name":"VS Code","duration_seconds":300}""");

        var usage = ActivityEventParser.ParseAppUsage(envelope, TenantId, EmployeeId);

        usage.Should().NotBeNull();
        usage!.Date.Should().Be(DateOnly.FromDateTime(capturedAt.UtcDateTime));
        usage.ProcessName.Should().Be("code.exe");
        usage.TotalSeconds.Should().Be(300);
    }
}
