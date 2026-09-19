namespace ONEVO.Application.Features.Monitoring.TrayActivation.Options;

public sealed class TrayPresenceOptions
{
    /// <summary>
    /// "Enforce" (default): gate users for whom <c>ITrayPresenceRequirementEvaluator</c> says a tray is required.
    /// "Observe": log would-be blocks only. "Off": never gate. Who is gated is decided by data, not by this value.
    /// </summary>
    public string Mode { get; set; } = "Enforce";
    public int GracePeriodSeconds { get; set; } = 120;
    public int HeartbeatSeconds { get; set; } = 30;
    public int AuthorizationRetentionDays { get; set; } = 30;
}
