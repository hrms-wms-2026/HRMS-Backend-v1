using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.LinkIntegrationModule;
public sealed record LinkIntegrationModuleCommand(string IntegrationKey, string ModuleKey, Guid ActorPlatformUserId) : IRequest<Result>;
public sealed class LinkIntegrationModuleCommandHandler : IRequestHandler<LinkIntegrationModuleCommand, Result>
{
    private readonly IIntegrationCatalogRepository _repo;
    private readonly IModuleCatalogRepository _modules;

    public LinkIntegrationModuleCommandHandler(
        IIntegrationCatalogRepository repo,
        IModuleCatalogRepository modules)
    {
        _repo = repo;
        _modules = modules;
    }

    public async Task<Result> Handle(LinkIntegrationModuleCommand request, CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var integration = await _repo.GetByKeyAsync(integrationKey, ct);
        if (integration is null)
        {
            return Result.NotFound($"Integration '{integrationKey}' was not found.");
        }

        if (!integration.IsActive)
        {
            return Result.Conflict($"Integration '{integrationKey}' must be active before it can be linked.");
        }

        var module = await _modules.GetByKeyAsync(request.ModuleKey, ct);
        if (module is null)
        {
            return Result.NotFound($"Module '{request.ModuleKey}' was not found.");
        }

        var existingLink = await _repo.GetLinkAsync(request.ModuleKey, integrationKey, ct);
        if (existingLink is not null)
        {
            return Result.Conflict($"Integration '{integrationKey}' is already linked to module '{request.ModuleKey}'.");
        }

        var link = new ModuleIntegrationLink
        {
            ModuleKey = request.ModuleKey,
            IntegrationKey = integrationKey,
            LinkedById = request.ActorPlatformUserId,
            LinkedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddLinkAsync(link, ct);
        await _repo.SaveChangesAsync(ct);

        return Result.Success();
    }
}
