using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UpdateIntegration;

public sealed record UpdateIntegrationCommand(string IntegrationKey, string DisplayName, string? Description, string ConnectionScope, string OnevoAppProvider, string? LogoUrl, bool IsActive) : IRequest<Result<IntegrationCatalogDto>>;
public sealed class UpdateIntegrationCommandHandler : IRequestHandler<UpdateIntegrationCommand, Result<IntegrationCatalogDto>>
{
    private readonly IIntegrationCatalogRepository _repo;
    private readonly IPlatformOAuthAppRepository _oauthApps;

    public UpdateIntegrationCommandHandler(
        IIntegrationCatalogRepository repo,
        IPlatformOAuthAppRepository oauthApps)
    {
        _repo = repo;
        _oauthApps = oauthApps;
    }

    public async Task<Result<IntegrationCatalogDto>> Handle(UpdateIntegrationCommand request, CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var provider = IntegrationCatalogRules.Normalize(request.OnevoAppProvider);

        var entity = await _repo.GetByKeyAsync(integrationKey, ct);
        if (entity is null)
        {
            return Result<IntegrationCatalogDto>.NotFound(
                $"Integration '{integrationKey}' was not found.");
        }

        if (IntegrationCatalogRules.IsForbidden(integrationKey))
        {
            return Result<IntegrationCatalogDto>.Failure(
                "This integration is not permitted in the Phase 1 integration catalog.",
                400);
        }

        var validationError = IntegrationCatalogRules.ValidateMetadata(
            request.DisplayName,
            request.ConnectionScope,
            provider,
            request.LogoUrl);

        if (validationError is not null)
        {
            return Result<IntegrationCatalogDto>.Failure(validationError, 400);
        }

        var oauthApp = await _oauthApps.GetByProviderAsync(provider, ct);
        if (oauthApp is null)
        {
            return Result<IntegrationCatalogDto>.Failure(
                $"ONEVO OAuth app provider '{provider}' does not exist.",
                400);
        }

        entity.DisplayName = request.DisplayName.Trim();
        entity.Description = request.Description?.Trim();
        entity.ConnectionScope = request.ConnectionScope;
        entity.OnevoAppProvider = provider;
        entity.LogoUrl = request.LogoUrl?.Trim();
        entity.IsActive = request.IsActive;

        await _repo.SaveChangesAsync(ct);

        var linkedModuleKeys = await _repo.GetLinkedModuleKeysAsync(integrationKey, ct);
        var dto = IntegrationCatalogMapper.ToDto(entity, linkedModuleKeys);

        return Result<IntegrationCatalogDto>.Success(dto);
    }
}
