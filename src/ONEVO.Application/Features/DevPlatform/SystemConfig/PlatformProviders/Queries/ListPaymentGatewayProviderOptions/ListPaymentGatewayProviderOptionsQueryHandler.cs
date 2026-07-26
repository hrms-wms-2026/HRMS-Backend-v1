using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListPaymentGatewayProviderOptions;

/// <summary>
/// Provider selection options for the System Config payment gateway screen: payment_gateway family only.
/// </summary>
public sealed record ListPaymentGatewayProviderOptionsQuery
    : IRequest<Result<IReadOnlyList<ProviderOptionDto>>>;

public sealed class ListPaymentGatewayProviderOptionsQueryHandler
    : IRequestHandler<ListPaymentGatewayProviderOptionsQuery, Result<IReadOnlyList<ProviderOptionDto>>>
{
    private static readonly IReadOnlySet<string> AllowedFamilies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            PlatformProviderFamilies.PaymentGateway
        };

    private readonly IPlatformProviderRepository _repository;

    public ListPaymentGatewayProviderOptionsQueryHandler(IPlatformProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ProviderOptionDto>>> Handle(
        ListPaymentGatewayProviderOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var cards = await _repository.ListActiveCardsAsync(cancellationToken);

        var options = ProviderOptionMapper.ToOptions(cards, AllowedFamilies);

        return Result<IReadOnlyList<ProviderOptionDto>>.Success(options);
    }
}
