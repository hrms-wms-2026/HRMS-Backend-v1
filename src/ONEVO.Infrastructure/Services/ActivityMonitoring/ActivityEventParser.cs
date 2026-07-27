using System.Text.Json;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Services.ActivityMonitoring;

internal static class ActivityEventParser
{
    private const int MaxExpectedInputEvents = 3000;

    internal static ActivitySnapshot ParseActivitySnapshot(
        AgentEventEnvelope envelope, Guid tenantId, Guid employeeId)
    {
        var data = envelope.Data;
        var keyboardCount = data.TryGetProperty("keyboard_events_count", out var k) ? k.GetInt32() : 0;
        var mouseCount = data.TryGetProperty("mouse_events_count", out var m) ? m.GetInt32() : 0;
        var activeSeconds = data.TryGetProperty("active_seconds", out var a) ? a.GetInt32() : 0;
        var idleSeconds = data.TryGetProperty("idle_seconds", out var i) ? i.GetInt32() : 0;
        var processName = data.TryGetProperty("foreground_process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;

        var intensity = Math.Min((decimal)(keyboardCount + mouseCount) / MaxExpectedInputEvents * 100, 100);

        return new ActivitySnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CapturedAt = envelope.CapturedAt,
            KeyboardEventsCount = keyboardCount,
            MouseEventsCount = mouseCount,
            ActiveSeconds = activeSeconds,
            IdleSeconds = idleSeconds,
            IntensityScore = intensity,
            ForegroundProcessName = processName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    internal static MeetingSession? ParseMeetingAppUsage(
        AgentEventEnvelope envelope, Guid tenantId, Guid employeeId)
    {
        var data = envelope.Data;
        var processName = data.TryGetProperty("process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;

        if (!data.TryGetProperty("start", out var startEl) ||
            !DateTimeOffset.TryParse(startEl.GetString(), out var start))
            return null;

        if (!data.TryGetProperty("end", out var endEl) ||
            !DateTimeOffset.TryParse(endEl.GetString(), out var end))
            return null;

        var cameraActive = data.TryGetProperty("camera_active", out var cam) && cam.GetBoolean();
        var micActive = data.TryGetProperty("microphone_active", out var mic) && mic.GetBoolean();
        var platform = processName.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
        var durationMinutes = (int)Math.Round((end - start).TotalMinutes);

        return new MeetingSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            MeetingStart = start,
            MeetingEnd = end,
            Platform = platform,
            DurationMinutes = durationMinutes,
            HadCameraOn = cameraActive,
            HadMicActivity = micActive
        };
    }

    internal static ApplicationUsage? ParseAppUsage(
        AgentEventEnvelope envelope, Guid tenantId, Guid employeeId)
    {
        var data = envelope.Data;
        var processName = data.TryGetProperty("process_name", out var p) ? p.GetString() ?? string.Empty : string.Empty;
        var appName = data.TryGetProperty("application_name", out var a) ? a.GetString() ?? string.Empty : string.Empty;
        var category = data.TryGetProperty("app_category_type", out var c) ? c.GetString() : null;
        var titleHash = data.TryGetProperty("window_title_hash", out var h) ? h.GetString() : null;
        var duration = data.TryGetProperty("duration_seconds", out var d) ? d.GetInt32() : 0;

        return new ApplicationUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Date = DateOnly.FromDateTime(envelope.CapturedAt.UtcDateTime),
            ProcessName = processName,
            ApplicationName = appName,
            ApplicationCategory = category,
            WindowTitleHash = titleHash,
            TotalSeconds = duration
        };
    }
}
