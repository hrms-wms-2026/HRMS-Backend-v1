using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.SetPlatformServiceKeyActivation;

/// <summary>
/// Activates (IsActive = true) or deactivates (IsActive = false) a platform service key.
/// Deactivated keys are never resolved for runtime use.
/// </summary>
public sealed record SetPlatformServiceKeyActivationCommand(
    string ServiceKey,
    bool IsActive,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformServiceKeyDto>>;

public sealed class SetPlatformServiceKeyActivationCommandHandler
    : IRequestHandler<SetPlatformServiceKeyActivationCommand, Result<PlatformServiceKeyDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;

    public SetPlatformServiceKeyActivationCommandHandler(IPlatformServiceKeyRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        SetPlatformServiceKeyActivationCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<PlatformServiceKeyDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        entity.IsActive = request.IsActive;
        entity.UpdatedById = request.ActorPlatformUserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
