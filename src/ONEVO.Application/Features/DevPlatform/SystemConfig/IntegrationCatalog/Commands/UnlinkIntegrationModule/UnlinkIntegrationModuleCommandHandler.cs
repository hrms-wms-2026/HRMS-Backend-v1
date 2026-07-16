using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UnlinkIntegrationModule;
public sealed record UnlinkIntegrationModuleCommand(string IntegrationKey, string ModuleKey) : IRequest<Result>;
public sealed class UnlinkIntegrationModuleCommandHandler : IRequestHandler<UnlinkIntegrationModuleCommand, Result>
{
    private readonly IIntegrationCatalogRepository _repo;

    public UnlinkIntegrationModuleCommandHandler(IIntegrationCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result> Handle(UnlinkIntegrationModuleCommand request, CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var integration = await _repo.GetByKeyAsync(integrationKey, ct);
        if (integration is null)
        {
            return Result.NotFound($"Integration '{integrationKey}' was not found.");
        }

        var link = await _repo.GetLinkAsync(request.ModuleKey, integrationKey, ct);
        if (link is null)
        {
            return Result.NotFound($"Integration '{integrationKey}' is not linked to module '{request.ModuleKey}'.");
        }

        await _repo.RemoveLinkAsync(link, ct);
        await _repo.SaveChangesAsync(ct);

        return Result.Success();
    }
}
