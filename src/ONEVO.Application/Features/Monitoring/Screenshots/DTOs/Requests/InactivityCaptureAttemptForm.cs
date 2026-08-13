using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Requests;

/// <summary>Multipart form binding for POST /api/v1/monitoring/tray/inactivity-attempts.</summary>
public sealed class InactivityCaptureAttemptForm
{
    [FromForm(Name = "attempt_id")]
    public Guid AttemptId { get; set; }

    [FromForm(Name = "policy_version")]
    public string PolicyVersion { get; set; } = string.Empty;

    [FromForm(Name = "idle_started_at")]
    public DateTimeOffset IdleStartedAt { get; set; }

    [FromForm(Name = "prompted_at")]
    public DateTimeOffset PromptedAt { get; set; }

    [FromForm(Name = "decision_at")]
    public DateTimeOffset? DecisionAt { get; set; }

    [FromForm(Name = "captured_at")]
    public DateTimeOffset? CapturedAt { get; set; }

    [FromForm(Name = "idle_duration_seconds")]
    public int IdleDurationSeconds { get; set; }

    [FromForm(Name = "monitor_count")]
    public int MonitorCount { get; set; }

    [FromForm(Name = "outcome")]
    public string Outcome { get; set; } = string.Empty;

    [FromForm(Name = "failure_code")]
    public string? FailureCode { get; set; }

    [FromForm(Name = "content_type")]
    public string? ContentType { get; set; }

    [FromForm(Name = "sha256")]
    public string? Sha256 { get; set; }

    [FromForm(Name = "virtual_bounds_x")]
    public int? VirtualBoundsX { get; set; }

    [FromForm(Name = "virtual_bounds_y")]
    public int? VirtualBoundsY { get; set; }

    [FromForm(Name = "virtual_bounds_width")]
    public int? VirtualBoundsWidth { get; set; }

    [FromForm(Name = "virtual_bounds_height")]
    public int? VirtualBoundsHeight { get; set; }

    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }
}
