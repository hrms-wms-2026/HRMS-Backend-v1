namespace ONEVO.Application.Features.Monitoring.TrayActivation.Options;

public sealed class TrayPresenceOptions
{
    public string Mode { get; set; } = "Observe";
    public int GracePeriodSeconds { get; set; } = 120;
    public int HeartbeatSeconds { get; set; } = 30;
    public int AuthorizationRetentionDays { get; set; } = 30;
}
