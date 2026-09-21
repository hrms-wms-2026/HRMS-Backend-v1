using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListServiceKeyProviderOptions;

/// <summary>
/// Provider selection options for the System Config service-key screen:
/// transactional_email, infrastructure, object_storage, ai_verification families.
/// </summary>
public sealed record ListServiceKeyProviderOptionsQuery
    : IRequest<Result<IReadOnlyList<ServiceKeyProviderOptionDto>>>;

public sealed class ListServiceKeyProviderOptionsQueryHandler
    : IRequestHandler<ListServiceKeyProviderOptionsQuery, Result<IReadOnlyList<ServiceKeyProviderOptionDto>>>
{
    private readonly IPlatformProviderRepository _repository;

    public ListServiceKeyProviderOptionsQueryHandler(IPlatformProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ServiceKeyProviderOptionDto>>> Handle(
        ListServiceKeyProviderOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var cards = await _repository.ListActiveCardsAsync(cancellationToken);

        var options = ProviderOptionMapper.ToOptions(
            cards,
            PlatformProviderFamilies.PlatformServiceKeyFamilies);

        var withForms = new List<ServiceKeyProviderOptionDto>(options.Count);
        foreach (var option in options)
        {
            // A provider row with no definition cannot accept credentials, so the form
            // is empty and create/rotate reject it; an architecture test keeps this from shipping.
            var definition = ServiceKeyDefinitionRegistry.Find(option.ProviderKey);
            withForms.Add(new ServiceKeyProviderOptionDto
            {
                ProviderKey = option.ProviderKey,
                DisplayName = option.DisplayName,
                Configured = option.Configured,
                IsActive = option.IsActive,
                VerificationMode = definition?.Verification == ServiceKeyVerificationMode.Live
                    ? "live"
                    : "format-only",
                Fields = definition is null
                    ? []
                    : definition.Fields.Select(f => new ServiceKeyFieldDto
                    {
                        Name = f.Name,
                        Label = f.Label,
                        Kind = f.Kind,
                        Required = f.Required,
                        Placeholder = f.Placeholder,
                        DefaultValue = f.DefaultValue,
                        Options = (f.Options ?? []).Select(o => new ServiceKeyFieldOptionDto
                        {
                            Value = o.Value,
                            Label = o.Label
                        }).ToList()
                    }).ToList()
            });
        }

        return Result<IReadOnlyList<ServiceKeyProviderOptionDto>>.Success(withForms);
    }
}
