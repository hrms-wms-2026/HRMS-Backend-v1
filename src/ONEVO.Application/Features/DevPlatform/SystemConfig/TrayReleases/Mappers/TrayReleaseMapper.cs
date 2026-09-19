using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;

public static class TrayReleaseMapper
{
    public static TrayReleaseDto ToDto(TrayAppRelease r) => new(
        r.Id, r.Version, r.Channel, r.DownloadUrl, r.Sha256, r.FileSizeBytes, r.Publisher,
        r.MinimumWindowsVersion, r.MinSupportedVersion, r.ReleaseNotes, r.IsActive, r.Source,
        r.CreatedAt, r.UpdatedAt);

    public static TrayInstallerInfoDto ToInfo(TrayAppRelease r) => new(
        r.Version, r.DownloadUrl, r.Sha256, r.FileSizeBytes, r.Publisher,
        r.MinimumWindowsVersion, r.ReleaseNotes, r.Channel);
}
