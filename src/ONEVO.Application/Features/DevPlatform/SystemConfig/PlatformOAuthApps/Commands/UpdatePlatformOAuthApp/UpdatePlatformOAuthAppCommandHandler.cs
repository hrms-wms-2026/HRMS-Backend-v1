using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.UpdatePlatformOAuthApp;

/// <summary>
/// Updates non-secret OAuth app metadata only. Credential rows are never touched here;
/// secret changes must go through the rotate-secret command.
/// </summary>
public sealed record UpdatePlatformOAuthAppCommand(
    string Provider,
    string AppName,
    string? LogoUrl,
    string ClientId,
    string AuthorizationUrl,
    string TokenUrl,
    string[] DefaultScopes,
    bool IsActive,
    Guid ActorPlatformUserId) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class UpdatePlatformOAuthAppCommandHandler
    : IRequestHandler<UpdatePlatformOAuthAppCommand, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public UpdatePlatformOAuthAppCommandHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        UpdatePlatformOAuthAppCommand request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<PlatformOAuthAppDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        if (string.IsNullOrWhiteSpace(request.AppName) || request.AppName.Length > 100)
            return Result<PlatformOAuthAppDto>.Failure(
                "appName is required and must be at most 100 characters.", 400);

        if (string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 200)
            return Result<PlatformOAuthAppDto>.Failure(
                "clientId is required and must be at most 200 characters.", 400);

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(request.AuthorizationUrl)
            || request.AuthorizationUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "authorizationUrl is required and must be an absolute http/https URL (max 500 chars).", 400);

        if (!PlatformOAuthProviderRules.IsAbsoluteHttpUrl(request.TokenUrl)
            || request.TokenUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "tokenUrl is required and must be an absolute http/https URL (max 500 chars).", 400);

        if (request.LogoUrl is not null && request.LogoUrl.Length > 500)
            return Result<PlatformOAuthAppDto>.Failure(
                "logoUrl must be at most 500 characters.", 400);

        if (request.DefaultScopes.Length == 0
            || request.DefaultScopes.Any(string.IsNullOrWhiteSpace))
            return Result<PlatformOAuthAppDto>.Failure(
                "defaultScopes is required and must contain at least one non-empty scope.", 400);

        app.AppName = request.AppName.Trim();
        app.LogoUrl = request.LogoUrl;
        app.ClientId = request.ClientId.Trim();
        app.AuthorizationUrl = request.AuthorizationUrl.Trim();
        app.TokenUrl = request.TokenUrl.Trim();
        app.DefaultScopes = request.DefaultScopes.Select(s => s.Trim()).ToArray();
        app.IsActive = request.IsActive;
        app.UpdatedById = request.ActorPlatformUserId;
        app.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(cancellationToken);

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(app, activeCredentials.FirstOrDefault()));
    }
}
