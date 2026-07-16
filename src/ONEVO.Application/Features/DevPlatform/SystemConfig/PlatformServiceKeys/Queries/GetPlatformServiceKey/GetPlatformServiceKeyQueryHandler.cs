using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.GetPlatformServiceKey;

/// <summary>Detail lookup by service_key slug. Secrets are never included.</summary>
public sealed record GetPlatformServiceKeyQuery(string ServiceKey) : IRequest<Result<PlatformServiceKeyDto>>;

public sealed class GetPlatformServiceKeyQueryHandler
    : IRequestHandler<GetPlatformServiceKeyQuery, Result<PlatformServiceKeyDto>>
{
    private readonly IPlatformServiceKeyRepository _repo;

    public GetPlatformServiceKeyQueryHandler(IPlatformServiceKeyRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformServiceKeyDto>> Handle(
        GetPlatformServiceKeyQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByServiceKeyAsync(request.ServiceKey, cancellationToken);
        if (entity is null)
            return Result<PlatformServiceKeyDto>.NotFound(
                $"Platform service key '{request.ServiceKey}' was not found.");

        return Result<PlatformServiceKeyDto>.Success(PlatformServiceKeyMapper.ToDto(entity));
    }
}
