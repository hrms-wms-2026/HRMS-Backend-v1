using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Queries.ListPaymentGateways;

public sealed record ListPaymentGatewaysQuery : IRequest<Result<IReadOnlyList<PaymentGatewayConfigDto>>>;

public sealed class ListPaymentGatewaysQueryHandler
    : IRequestHandler<ListPaymentGatewaysQuery, Result<IReadOnlyList<PaymentGatewayConfigDto>>>
{
    private readonly IPaymentGatewayRepository _repo;

    public ListPaymentGatewaysQueryHandler(IPaymentGatewayRepository repo)
        => _repo = repo;

    public async Task<Result<IReadOnlyList<PaymentGatewayConfigDto>>> Handle(
        ListPaymentGatewaysQuery request,
        CancellationToken cancellationToken)
    {
        var configs = await _repo.ListAllAsync(cancellationToken);

        var dtos = new List<PaymentGatewayConfigDto>(configs.Count);
        foreach (var config in configs)
        {
            var activeCredential = await _repo.GetActiveCredentialAsync(config.Id, cancellationToken);
            var routes = await _repo.ListRoutesForConfigAsync(config.Id, cancellationToken);

            dtos.Add(MapToDto(config, activeCredential?.CredentialVersion ?? -1, activeCredential != null, routes));
        }

        return Result<IReadOnlyList<PaymentGatewayConfigDto>>.Success(dtos);
    }

    private static PaymentGatewayConfigDto MapToDto(
        Domain.Features.SharedPlatform.PaymentGateway.Entities.PaymentGatewayConfig config,
        int activeVersion,
        bool hasActiveCredential,
        IReadOnlyList<Domain.Features.SharedPlatform.PaymentGateway.Entities.PaymentGatewayCountryRoute> routes)
        => new()
        {
            Id = config.Id,
            GatewayKey = config.GatewayKey,
            Provider = config.Provider,
            Environment = config.Environment,
            DisplayName = config.DisplayName,
            LogoUrl = config.LogoUrl,
            PublicKey = config.PublicKey,       // public identifier - safe to return
            MerchantId = config.MerchantId,
            WebhookUrl = config.WebhookUrl,
            IsActive = config.IsActive,
            HasActiveCredential = hasActiveCredential,
            ActiveCredentialVersion = activeVersion,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            CountryRoutes = routes.Select(r => new PaymentGatewayCountryRouteDto
            {
                Id = r.Id,
                CountryCode = r.CountryCode,
                CountryNameSnapshot = r.CountryNameSnapshot,
                Environment = r.Environment,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
}
