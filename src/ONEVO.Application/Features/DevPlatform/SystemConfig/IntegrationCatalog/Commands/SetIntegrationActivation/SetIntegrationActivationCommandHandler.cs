using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.SetIntegrationActivation;
public sealed record SetIntegrationActivationCommand(string IntegrationKey, bool IsActive) : IRequest<Result<IntegrationCatalogDto>>;
public sealed class SetIntegrationActivationCommandHandler : IRequestHandler<SetIntegrationActivationCommand, Result<IntegrationCatalogDto>>
{
    private readonly IIntegrationCatalogRepository _repo;

    public SetIntegrationActivationCommandHandler(IIntegrationCatalogRepository repo)
    {
        _repo = repo;
    }

    public async Task<Result<IntegrationCatalogDto>> Handle(
        SetIntegrationActivationCommand request,
        CancellationToken ct)
    {
        var integrationKey = IntegrationCatalogRules.Normalize(request.IntegrationKey);
        var entity = await _repo.GetByKeyAsync(integrationKey, ct);
        if (entity is null)
        {
            return Result<IntegrationCatalogDto>.NotFound(
                $"Integration '{integrationKey}' was not found.");
        }

        entity.IsActive = request.IsActive;
        await _repo.SaveChangesAsync(ct);

        var linkedModuleKeys = await _repo.GetLinkedModuleKeysAsync(integrationKey, ct);
        var dto = IntegrationCatalogMapper.ToDto(entity, linkedModuleKeys);

        return Result<IntegrationCatalogDto>.Success(dto);
    }
}
