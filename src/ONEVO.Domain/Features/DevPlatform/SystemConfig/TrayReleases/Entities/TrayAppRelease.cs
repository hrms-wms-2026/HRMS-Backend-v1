namespace ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

/// <summary>
/// One published build of the ONEVO tray installer (.msix). Platform-level: no tenant, no RLS.
/// Replaces the static "TrayInstaller" appsettings block. Canonical table: tray_app_releases.
/// "Latest" for a channel is the highest x.y.z among is_active rows in that channel.
/// </summary>
public class TrayAppRelease
{
    public Guid Id { get; set; }

    /// <summary>Strict x.y.z (regex ^\d+\.\d+\.\d+$). Unique per channel.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>"stable" or "beta".</summary>
    public string Channel { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the installer file (64 chars).</summary>
    public string Sha256 { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>Certificate subject shown to the user, e.g. "CN=ONEVO".</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>e.g. "10.0.19041.0".</summary>
    public string MinimumWindowsVersion { get; set; } = string.Empty;

    /// <summary>Trays older than this must update before continuing. Null = no forced update.</summary>
    public string? MinSupportedVersion { get; set; }

    public string? ReleaseNotes { get; set; }

    /// <summary>Only active rows are ever served to employees or trays.</summary>
    public bool IsActive { get; set; }

    /// <summary>"admin" (created in the admin app) or "ci" (registered by the release pipeline).</summary>
    public string Source { get; set; } = "admin";

    /// <summary>Platform user who created the row; null when Source = "ci".</summary>
    public Guid? CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
