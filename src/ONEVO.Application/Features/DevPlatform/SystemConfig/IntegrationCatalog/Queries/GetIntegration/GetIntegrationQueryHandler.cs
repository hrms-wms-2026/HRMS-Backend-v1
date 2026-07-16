using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Queries.GetIntegration;
public sealed record GetIntegrationQuery(string IntegrationKey) : IRequest<Result<IntegrationCatalogDto>>;
public sealed class GetIntegrationQueryHandler : IRequestHandler<GetIntegrationQuery, Result<IntegrationCatalogDto>>
{
    private readonly IIntegrationCatalogRepository _repo;

    public GetIntegrationQueryHandler(IIntegrationCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IntegrationCatalogDto>> Handle(
        GetIntegrationQuery request,
        CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var entry = await _repo.GetByKeyAsync(integrationKey, ct);
        if (entry is null)
        {
            return Result<IntegrationCatalogDto>.NotFound(
                $"Integration '{integrationKey}' was not found.");
        }

        var linkedModuleKeys = await _repo.GetLinkedModuleKeysAsync(integrationKey, ct);
        var dto = IntegrationCatalogMapper.ToDto(entry, linkedModuleKeys);

        return Result<IntegrationCatalogDto>.Success(dto);
    }
}
