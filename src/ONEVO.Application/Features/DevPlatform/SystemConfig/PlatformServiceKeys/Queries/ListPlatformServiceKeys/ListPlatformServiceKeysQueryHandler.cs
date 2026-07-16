using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.ListPlatformServiceKeys;

/// <summary>Lists all platform service keys. Secrets are never included.</summary>
public sealed record ListPlatformServiceKeysQuery() : IRequest<Result<IReadOnlyList<PlatformServiceKeyDto>>>;

public sealed class ListPlatformServiceKeysQueryHandler
    : IRequestHandler<ListPlatformServiceKeysQuery, Result<IReadOnlyList<PlatformServiceKeyDto>>>
{
    private readonly IPlatformServiceKeyRepository _repo;

    public ListPlatformServiceKeysQueryHandler(IPlatformServiceKeyRepository repo)
        => _repo = repo;

    public async Task<Result<IReadOnlyList<PlatformServiceKeyDto>>> Handle(
        ListPlatformServiceKeysQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await _repo.ListAllAsync(cancellationToken);

        var dtos = new List<PlatformServiceKeyDto>(entities.Count);
        foreach (var entity in entities)
            dtos.Add(PlatformServiceKeyMapper.ToDto(entity));

        return Result<IReadOnlyList<PlatformServiceKeyDto>>.Success(dtos);
    }
}
