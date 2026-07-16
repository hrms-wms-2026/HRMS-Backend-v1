using MediatR;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;

public class GetModuleCatalogDetailQueryHandler : IRequestHandler<GetModuleCatalogDetailQuery, Result<ModuleCatalogDetailDto>>
{
    private readonly IModuleCatalogRepository _repo;

    public GetModuleCatalogDetailQueryHandler(IModuleCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<ModuleCatalogDetailDto>> Handle(GetModuleCatalogDetailQuery request, CancellationToken cancellationToken)
    {
        var m = await _repo.GetByKeyAsync(request.ModuleKey, cancellationToken);

        if (m is null)
        {
            return Result<ModuleCatalogDetailDto>.NotFound($"Module '{request.ModuleKey}' not found.");
        }

        var dto = new ModuleCatalogDetailDto(
            m.ModuleKey,
            m.Name,
            m.Pillar,
            m.Phase,
            m.PricingUnit,
            m.PricingReference,
            m.StorageReference,
            m.AiTokenReference,
            m.IsAiEnabled,
            m.IsStorageConsuming,
            m.IsActive,
            m.CreatedAt,
            m.UpdatedAt);

        return Result<ModuleCatalogDetailDto>.Success(dto);
    }
}
