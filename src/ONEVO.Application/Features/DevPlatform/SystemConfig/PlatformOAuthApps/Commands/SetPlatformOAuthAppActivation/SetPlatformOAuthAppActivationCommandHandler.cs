using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.SetPlatformOAuthAppActivation;

/// <summary>
/// Activates or deactivates an OAuth app registration.
/// Activation requires at least one active credential; deactivation never deletes
/// credential rows.
/// </summary>
public sealed record SetPlatformOAuthAppActivationCommand(
    string Provider,
    bool IsActive,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class SetPlatformOAuthAppActivationCommandHandler
    : IRequestHandler<SetPlatformOAuthAppActivationCommand, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public SetPlatformOAuthAppActivationCommandHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        SetPlatformOAuthAppActivationCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<PlatformOAuthAppDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);

        // Activation requires a usable credential; deactivation is always allowed.
        if (request.IsActive && activeCredentials.Count == 0)
            return Result<PlatformOAuthAppDto>.Failure(
                $"OAuth app '{provider}' cannot be activated without an active credential. Rotate its secret first.", 400);

        app.IsActive = request.IsActive;
        app.UpdatedById = request.ActorPlatformUserId;
        app.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(app, activeCredentials.FirstOrDefault()));
    }
}
