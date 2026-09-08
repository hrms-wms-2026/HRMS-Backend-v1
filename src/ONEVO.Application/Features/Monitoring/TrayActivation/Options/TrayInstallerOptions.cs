namespace ONEVO.Application.Features.Monitoring.TrayActivation.Options;

public sealed class TrayInstallerOptions
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string MinimumWindowsVersion { get; set; } = string.Empty;
}
