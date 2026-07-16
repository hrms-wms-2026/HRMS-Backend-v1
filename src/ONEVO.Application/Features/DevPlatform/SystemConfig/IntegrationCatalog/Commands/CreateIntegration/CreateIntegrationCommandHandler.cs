using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.CreateIntegration;

public sealed record CreateIntegrationCommand(string IntegrationKey, string DisplayName, string? Description,
    string ConnectionScope, string OnevoAppProvider, string? LogoUrl, bool IsActive, Guid ActorPlatformUserId)
    : IRequest<Result<IntegrationCatalogDto>>;

public sealed class CreateIntegrationCommandHandler : IRequestHandler<CreateIntegrationCommand, Result<IntegrationCatalogDto>>
{
    private readonly IIntegrationCatalogRepository _repo;
    private readonly IPlatformOAuthAppRepository _oauthApps;

    public CreateIntegrationCommandHandler(
        IIntegrationCatalogRepository repo,
        IPlatformOAuthAppRepository oauthApps)
    {
        _repo = repo;
        _oauthApps = oauthApps;
    }

    public async Task<Result<IntegrationCatalogDto>> Handle(CreateIntegrationCommand request, CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var provider = IntegrationCatalogRules.Normalize(request.OnevoAppProvider);

        if (!IntegrationCatalogRules.IsValidSlug(request.IntegrationKey)
            || request.IntegrationKey != integrationKey)
        {
            return Result<IntegrationCatalogDto>.Failure(
                "integrationKey must be a lowercase slug using letters, digits, and underscores (max 50 characters).",
                400);
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

        var existingIntegration = await _repo.GetByKeyAsync(integrationKey, ct);
        if (existingIntegration is not null)
        {
            return Result<IntegrationCatalogDto>.Conflict(
                $"Integration '{integrationKey}' already exists.");
        }

        var oauthApp = await _oauthApps.GetByProviderAsync(provider, ct);
        if (oauthApp is null)
        {
            return Result<IntegrationCatalogDto>.Failure(
                $"ONEVO OAuth app provider '{provider}' does not exist.",
                400);
        }

        var entity = new IntegrationCatalogEntry
        {
            IntegrationKey = integrationKey,
            DisplayName = request.DisplayName.Trim(),
            Description = request.Description?.Trim(),
            ConnectionScope = request.ConnectionScope,
            OnevoAppProvider = provider,
            LogoUrl = request.LogoUrl?.Trim(),
            IsActive = request.IsActive,
            CreatedById = request.ActorPlatformUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        var dto = IntegrationCatalogMapper.ToDto(entity, Array.Empty<string>());
        return Result<IntegrationCatalogDto>.Success(dto);
    }
}
