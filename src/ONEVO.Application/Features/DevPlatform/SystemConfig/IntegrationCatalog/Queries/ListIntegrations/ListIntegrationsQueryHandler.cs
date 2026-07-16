using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Queries.ListIntegrations;
public sealed record ListIntegrationsQuery : IRequest<Result<IReadOnlyList<IntegrationCatalogDto>>>;
public sealed class ListIntegrationsQueryHandler : IRequestHandler<ListIntegrationsQuery, Result<IReadOnlyList<IntegrationCatalogDto>>>
{
    private readonly IIntegrationCatalogRepository _repo;

    public ListIntegrationsQueryHandler(IIntegrationCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<IntegrationCatalogDto>>> Handle(
        ListIntegrationsQuery request,
        CancellationToken ct)
    {
        var entries = await _repo.ListAllAsync(ct);
        var links = await _repo.ListAllLinksAsync(ct);
        var integrationDtos = new List<IntegrationCatalogDto>(entries.Count);

        foreach (var entry in entries)
        {
            var linkedModuleKeys = links
                .Where(link => link.IntegrationKey == entry.IntegrationKey)
                .Select(link => link.ModuleKey)
                .OrderBy(moduleKey => moduleKey)
                .ToArray();

            var dto = IntegrationCatalogMapper.ToDto(entry, linkedModuleKeys);
            integrationDtos.Add(dto);
        }

        return Result<IReadOnlyList<IntegrationCatalogDto>>.Success(integrationDtos);
    }
}
