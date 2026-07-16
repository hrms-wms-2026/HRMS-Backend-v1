using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Queries.ResolveGatewayForCountry;

/// <summary>
/// Resolves the active payment gateway for a given country+environment.
/// Used by tenant provisioning Step 3 to determine which gateway applies.
/// </summary>
public sealed record ResolveGatewayForCountryQuery(
    string CountryCode,
    string Environment) : IRequest<Result<ResolvedGatewayDto>>;

public sealed class ResolveGatewayForCountryQueryHandler
    : IRequestHandler<ResolveGatewayForCountryQuery, Result<ResolvedGatewayDto>>
{
    private readonly IPaymentGatewayRepository _repo;

    public ResolveGatewayForCountryQueryHandler(IPaymentGatewayRepository repo)
        => _repo = repo;

    public async Task<Result<ResolvedGatewayDto>> Handle(
        ResolveGatewayForCountryQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Length != 2)
            return Result<ResolvedGatewayDto>.Failure(
                "Country code must be a 2-character ISO 3166-1 alpha-2 code.", 400);

        var config = await _repo.ResolveForCountryAsync(
            request.CountryCode.ToUpperInvariant(),
            request.Environment,
            cancellationToken);

        if (config is null)
        {
            return Result<ResolvedGatewayDto>.Success(new ResolvedGatewayDto
            {
                IsResolved = false,
                GatewayConfigId = Guid.Empty,
                GatewayKey = string.Empty,
                Provider = string.Empty,
                DisplayName = string.Empty
            });
        }

        return Result<ResolvedGatewayDto>.Success(new ResolvedGatewayDto
        {
            GatewayConfigId = config.Id,
            GatewayKey = config.GatewayKey,
            Provider = config.Provider,
            DisplayName = config.DisplayName,
            LogoUrl = config.LogoUrl,
            IsResolved = true
        });
    }
}
