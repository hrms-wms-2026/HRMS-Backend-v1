using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;

/// <summary>Creates a release row. Used by the admin form (Source "admin") and CI ingest (Source "ci").</summary>
public sealed record CreateTrayReleaseCommand(
    CreateTrayReleaseRequest Request,
    string Source,
    Guid? ActorPlatformUserId) : IRequest<Result<TrayReleaseDto>>;

public sealed class CreateTrayReleaseCommandHandler
    : IRequestHandler<CreateTrayReleaseCommand, Result<TrayReleaseDto>>
{
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    private readonly ITrayAppReleaseRepository _repo;

    public CreateTrayReleaseCommandHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayReleaseDto>> Handle(
        CreateTrayReleaseCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        if (!TrayReleaseVersion.TryParse(req.Version, out _))
            return Result<TrayReleaseDto>.Failure("version must be x.y.z (numbers only).", 400);
        if (!TrayReleaseChannels.IsValid(req.Channel))
            return Result<TrayReleaseDto>.Failure("channel must be 'stable' or 'beta'.", 400);
        if (!Uri.TryCreate(req.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Result<TrayReleaseDto>.Failure("downloadUrl must be an absolute https URL.", 400);
        if (string.IsNullOrWhiteSpace(req.Sha256) || !Sha256Hex.IsMatch(req.Sha256))
            return Result<TrayReleaseDto>.Failure("sha256 must be 64 hex characters.", 400);
        if (req.FileSizeBytes <= 0)
            return Result<TrayReleaseDto>.Failure("fileSizeBytes must be greater than zero.", 400);
        if (string.IsNullOrWhiteSpace(req.Publisher) || req.Publisher.Length > 200)
            return Result<TrayReleaseDto>.Failure("publisher is required (max 200 characters).", 400);
        if (string.IsNullOrWhiteSpace(req.MinimumWindowsVersion))
            return Result<TrayReleaseDto>.Failure("minimumWindowsVersion is required.", 400);
        if (!string.IsNullOrWhiteSpace(req.MinSupportedVersion) &&
            !TrayReleaseVersion.TryParse(req.MinSupportedVersion, out _))
            return Result<TrayReleaseDto>.Failure("minSupportedVersion must be x.y.z (numbers only).", 400);

        var existing = await _repo.GetByChannelAndVersionAsync(req.Channel, req.Version, cancellationToken);
        if (existing is not null)
            return Result<TrayReleaseDto>.Conflict(
                $"Version {req.Version} already exists in channel '{req.Channel}'.");

        var now = DateTimeOffset.UtcNow;
        var entity = new TrayAppRelease
        {
            Id = Guid.NewGuid(),
            Version = req.Version,
            Channel = req.Channel,
            DownloadUrl = req.DownloadUrl,
            Sha256 = req.Sha256.ToLowerInvariant(),
            FileSizeBytes = req.FileSizeBytes,
            Publisher = req.Publisher.Trim(),
            MinimumWindowsVersion = req.MinimumWindowsVersion.Trim(),
            MinSupportedVersion = string.IsNullOrWhiteSpace(req.MinSupportedVersion) ? null : req.MinSupportedVersion,
            ReleaseNotes = string.IsNullOrWhiteSpace(req.ReleaseNotes) ? null : req.ReleaseNotes,
            IsActive = req.IsActive,
            Source = command.Source,
            CreatedById = command.ActorPlatformUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<TrayReleaseDto>.Success(TrayReleaseMapper.ToDto(entity));
    }
}
