using MediatR;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;

public class GetModuleCatalogListQueryHandler : IRequestHandler<GetModuleCatalogListQuery, Result<IReadOnlyList<ModuleCatalogListDto>>>
{
    private readonly IModuleCatalogRepository _repo;

    public GetModuleCatalogListQueryHandler(IModuleCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IReadOnlyList<ModuleCatalogListDto>>> Handle(GetModuleCatalogListQuery request, CancellationToken cancellationToken)
    {
        var modules = await _repo.GetAllAsync(cancellationToken);
        
        var dtoList = modules.Select(m => new ModuleCatalogListDto(
                m.ModuleKey,
                m.Name,
                m.Pillar,
                m.Phase,
                m.PricingUnit,
                m.IsActive))
            .ToList();

        return Result<IReadOnlyList<ModuleCatalogListDto>>.Success(dtoList);
    }
}
