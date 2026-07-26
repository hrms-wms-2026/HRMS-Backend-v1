using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListServiceKeyProviderOptions;

/// <summary>
/// Provider selection options for the System Config service-key screen:
/// transactional_email, infrastructure, object_storage, ai_verification families.
/// </summary>
public sealed record ListServiceKeyProviderOptionsQuery
    : IRequest<Result<IReadOnlyList<ProviderOptionDto>>>;

public sealed class ListServiceKeyProviderOptionsQueryHandler
    : IRequestHandler<ListServiceKeyProviderOptionsQuery, Result<IReadOnlyList<ProviderOptionDto>>>
{
    private readonly IPlatformProviderRepository _repository;

    public ListServiceKeyProviderOptionsQueryHandler(IPlatformProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ProviderOptionDto>>> Handle(
        ListServiceKeyProviderOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var cards = await _repository.ListActiveCardsAsync(cancellationToken);

        var options = ProviderOptionMapper.ToOptions(
            cards,
            PlatformProviderFamilies.PlatformServiceKeyFamilies);

        return Result<IReadOnlyList<ProviderOptionDto>>.Success(options);
    }
}
