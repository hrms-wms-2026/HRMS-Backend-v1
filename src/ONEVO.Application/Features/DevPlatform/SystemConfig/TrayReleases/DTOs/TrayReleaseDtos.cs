using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;

public static class TrayReleaseChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";
    public static bool IsValid(string? channel) => channel is Stable or Beta;
}

public static class TrayReleaseSources
{
    public const string Admin = "admin";
    public const string Ci = "ci";
}

/// <summary>Admin-facing row (camelCase JSON).</summary>
public sealed record TrayReleaseDto(
    Guid Id, string Version, string Channel, string DownloadUrl, string Sha256,
    long FileSizeBytes, string Publisher, string MinimumWindowsVersion,
    string? MinSupportedVersion, string? ReleaseNotes, bool IsActive, string Source,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Public installer metadata (snake_case JSON, same field names the old TrayInstallerResponseDto used).</summary>
public sealed record TrayInstallerInfoDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("minimum_windows_version")] string MinimumWindowsVersion,
    [property: JsonPropertyName("release_notes")] string? ReleaseNotes,
    [property: JsonPropertyName("channel")] string Channel);

public sealed record TrayUpdateCheckDto(
    [property: JsonPropertyName("update_available")] bool UpdateAvailable,
    [property: JsonPropertyName("mandatory")] bool Mandatory,
    [property: JsonPropertyName("latest")] TrayInstallerInfoDto? Latest);

public sealed record CreateTrayReleaseRequest(
    string Version, string Channel, string DownloadUrl, string Sha256, long FileSizeBytes,
    string Publisher, string MinimumWindowsVersion, string? MinSupportedVersion,
    string? ReleaseNotes, bool IsActive);

public sealed record UpdateTrayReleaseRequest(
    string? Channel, string? MinSupportedVersion, string? ReleaseNotes, bool? IsActive);
