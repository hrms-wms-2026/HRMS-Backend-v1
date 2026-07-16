using MediatR;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;

public class GetModuleCatalogFeaturesQueryHandler : IRequestHandler<GetModuleCatalogFeaturesQuery, Result<IReadOnlyList<ModuleFeatureDto>>>
{
    private readonly IModuleCatalogRepository _repo;

    public GetModuleCatalogFeaturesQueryHandler(IModuleCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<ModuleFeatureDto>>> Handle(GetModuleCatalogFeaturesQuery request, CancellationToken cancellationToken)
    {
        var module = await _repo.GetByKeyAsync(request.ModuleKey, cancellationToken);

        if (module is null)
        {
            return Result<IReadOnlyList<ModuleFeatureDto>>.NotFound($"Module '{request.ModuleKey}' not found.");
        }

        var features = module.Features
            .Select(f => new ModuleFeatureDto(
                f.FeatureKey,
                f.Name,
                f.Description,
                f.IsDefaultIncluded,
                f.IsActive))
            .OrderBy(f => f.FeatureKey)
            .ToList();

        return Result<IReadOnlyList<ModuleFeatureDto>>.Success(features);
    }
}
