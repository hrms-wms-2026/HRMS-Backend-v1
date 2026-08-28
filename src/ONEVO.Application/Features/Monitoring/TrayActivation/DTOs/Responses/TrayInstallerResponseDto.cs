using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public sealed record TrayInstallerResponseDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("minimum_windows_version")] string MinimumWindowsVersion);
