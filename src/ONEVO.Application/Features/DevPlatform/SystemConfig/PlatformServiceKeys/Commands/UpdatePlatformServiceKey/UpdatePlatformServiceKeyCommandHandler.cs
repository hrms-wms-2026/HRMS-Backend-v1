using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.UpdatePlatformServiceKey;

/// <summary>
/// Metadata-only update (display name). Activation changes use the dedicated
/// activate/deactivate commands; key material changes use rotate-key.
/// </summary>
public sealed record UpdatePlatformServiceKeyCommand(
    string ServiceKey,
    string DisplayName,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformServiceKeyDto>>;

public sealed class UpdatePlatformServiceKeyCommandHandler
    : IRequestHandler<UpdatePlatformServiceKeyCommand, Result<PlatformServiceKeyDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;

    public UpdatePlatformServiceKeyCommandHandler(IPlatformServiceKeyRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        UpdatePlatformServiceKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 80)
            return Result<PlatformServiceKeyDto>.Failure(
                "displayName is required and must be at most 80 characters.", 400);

        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<PlatformServiceKeyDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        entity.DisplayName = request.DisplayName.Trim();
        entity.UpdatedById = request.ActorPlatformUserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
