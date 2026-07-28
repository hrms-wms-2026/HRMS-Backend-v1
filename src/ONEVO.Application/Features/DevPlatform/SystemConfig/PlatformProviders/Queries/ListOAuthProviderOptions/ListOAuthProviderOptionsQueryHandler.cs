using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListOAuthProviderOptions;

/// <summary>
/// Provider selection options for the System Config OAuth app screen: oauth_app family only.
/// </summary>
public sealed record ListOAuthProviderOptionsQuery
    : IRequest<Result<IReadOnlyList<ProviderOptionDto>>>;

public sealed class ListOAuthProviderOptionsQueryHandler
    : IRequestHandler<ListOAuthProviderOptionsQuery, Result<IReadOnlyList<ProviderOptionDto>>>
{
    private static readonly IReadOnlySet<string> AllowedFamilies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            PlatformProviderFamilies.OAuthApp
        };

    private readonly IPlatformProviderRepository _repository;

    public ListOAuthProviderOptionsQueryHandler(IPlatformProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ProviderOptionDto>>> Handle(
        ListOAuthProviderOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var cards = await _repository.ListActiveCardsAsync(cancellationToken);

        var options = ProviderOptionMapper.ToOptions(cards, AllowedFamilies);

        return Result<IReadOnlyList<ProviderOptionDto>>.Success(options);
    }
}
