namespace ONEVO.Application.Features.Monitoring.Screenshots;

/// <summary>Stable outcome codes for inactivity capture attempts (mirrors tray shared contract).</summary>
public static class InactivityCaptureOutcomes
{
    public const string Captured = "captured";
    public const string Declined = "declined";
    public const string TimedOut = "timed_out";
    public const string ActivityResumed = "activity_resumed";
    public const string MonitoringStopped = "monitoring_stopped";
    public const string CaptureFailed = "capture_failed";
}
