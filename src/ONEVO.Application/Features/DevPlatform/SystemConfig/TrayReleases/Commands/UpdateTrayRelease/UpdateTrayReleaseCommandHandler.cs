using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;

/// <summary>
/// Partial update: change channel (promote beta→stable), min supported version, notes, or activate/deactivate.
/// Pass null to leave a field unchanged; pass "" for MinSupportedVersion/ReleaseNotes to clear.
/// </summary>
public sealed record UpdateTrayReleaseCommand(Guid Id, UpdateTrayReleaseRequest Request)
    : IRequest<Result<TrayReleaseDto>>;

public sealed class UpdateTrayReleaseCommandHandler
    : IRequestHandler<UpdateTrayReleaseCommand, Result<TrayReleaseDto>>
{
    private readonly ITrayAppReleaseRepository _repo;

    public UpdateTrayReleaseCommandHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayReleaseDto>> Handle(
        UpdateTrayReleaseCommand command, CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByIdAsync(command.Id, cancellationToken);
        if (entity is null)
            return Result<TrayReleaseDto>.NotFound("Tray release was not found.");

        var req = command.Request;

        if (req.Channel is not null && req.Channel != entity.Channel)
        {
            if (!TrayReleaseChannels.IsValid(req.Channel))
                return Result<TrayReleaseDto>.Failure("channel must be 'stable' or 'beta'.", 400);

            var clash = await _repo.GetByChannelAndVersionAsync(req.Channel, entity.Version, cancellationToken);
            if (clash is not null)
                return Result<TrayReleaseDto>.Conflict(
                    $"Version {entity.Version} already exists in channel '{req.Channel}'.");

            entity.Channel = req.Channel;
        }

        if (req.MinSupportedVersion is not null)
        {
            if (req.MinSupportedVersion.Length == 0)
                entity.MinSupportedVersion = null;
            else if (!TrayReleaseVersion.TryParse(req.MinSupportedVersion, out _))
                return Result<TrayReleaseDto>.Failure("minSupportedVersion must be x.y.z (numbers only).", 400);
            else
                entity.MinSupportedVersion = req.MinSupportedVersion;
        }

        if (req.ReleaseNotes is not null)
            entity.ReleaseNotes = req.ReleaseNotes.Length == 0 ? null : req.ReleaseNotes;

        if (req.IsActive is bool active)
            entity.IsActive = active;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.SaveChangesAsync(cancellationToken);
        return Result<TrayReleaseDto>.Success(TrayReleaseMapper.ToDto(entity));
    }
}
