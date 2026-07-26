using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListPlatformProviders;

public sealed record ListPlatformProvidersQuery
    : IRequest<Result<IReadOnlyList<PlatformProviderCardDto>>>;

public sealed class ListPlatformProvidersQueryHandler
    : IRequestHandler<ListPlatformProvidersQuery, Result<IReadOnlyList<PlatformProviderCardDto>>>
{
    private readonly IPlatformProviderRepository _repository;

    public ListPlatformProvidersQueryHandler(IPlatformProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<PlatformProviderCardDto>>> Handle(
        ListPlatformProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var cards = await _repository.ListActiveCardsAsync(cancellationToken);

        var response = cards.Select(card => new PlatformProviderCardDto
        {
            Id = card.Id,
            ProviderKey = card.ProviderKey,
            DisplayName = card.DisplayName,
            ProviderFamily = card.ProviderFamily,
            Configured = card.Configured,
            ConfigurationActive = card.ConfigurationActive,
            LastVerifiedAt = card.LastVerifiedAt
        }).ToList();

        return Result<IReadOnlyList<PlatformProviderCardDto>>.Success(response);
    }
}
