using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

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
    private readonly IPlatformProviderRepository _providerRepository;

    public SetPlatformServiceKeyActivationCommandHandler(
        IPlatformServiceKeyRepository repo,
        IPlatformProviderRepository providerRepository)
    {
        _repo = repo;
        _providerRepository = providerRepository;
    }

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        SetPlatformServiceKeyActivationCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<PlatformServiceKeyDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        var providerFamily = await _providerRepository.GetActiveFamilyAsync(
            request.ServiceKey,
            cancellationToken);
        if (providerFamily is null ||
            !PlatformProviderFamilies.PlatformServiceKeyFamilies.Contains(providerFamily))
        {
            return Result<PlatformServiceKeyDto>.Failure(
                $"Provider '{request.ServiceKey}' is not an active platform service-key provider.",
                400);
        }

        // Activating a transactional email provider must not conflict with another
        // already-active transactional email provider. The operator must explicitly
        // deactivate the current one first; the backend never auto-deactivates it.
        if (request.IsActive && providerFamily == PlatformProviderFamilies.TransactionalEmail)
        {
            var activeEmailProviders = await _repo.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                cancellationToken);
            foreach (var key in activeEmailProviders)
            {
                if (!string.Equals(key.ServiceKey, request.ServiceKey, StringComparison.Ordinal))
                {
                    return Result<PlatformServiceKeyDto>.Conflict(
                        $"Active transactional email provider '{key.ServiceKey}' already exists. " +
                        $"Deactivate it before activating '{request.ServiceKey}'.");
                }
            }
        }

        entity.IsActive = request.IsActive;
        entity.UpdatedById = request.ActorPlatformUserId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
