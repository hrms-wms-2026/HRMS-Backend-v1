using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.UpdatePaymentGatewayMetadata;

/// <summary>
/// Updates non-credential payment gateway metadata and replaces country routes when requested.
/// Credential secrets are never accepted here; use RotatePaymentGatewayCredentialCommand instead.
/// </summary>
public sealed record UpdatePaymentGatewayMetadataCommand(
    Guid GatewayConfigId,
    string? DisplayName,
    string? LogoUrl,
    string? PublicKey,
    string? MerchantId,
    string? WebhookUrl,
    bool? IsActive,
    IReadOnlyList<string>? CountryCodes,
    IReadOnlyList<string?>? CountryNameSnapshots,
    Guid ActorPlatformUserId) : IRequest<Result<PaymentGatewayConfigDto>>;

public sealed class UpdatePaymentGatewayMetadataCommandHandler
    : IRequestHandler<UpdatePaymentGatewayMetadataCommand, Result<PaymentGatewayConfigDto>>
{
    private readonly IPaymentGatewayRepository _repo;

    public UpdatePaymentGatewayMetadataCommandHandler(IPaymentGatewayRepository repo)
        => _repo = repo;

    public async Task<Result<PaymentGatewayConfigDto>> Handle(
        UpdatePaymentGatewayMetadataCommand request,
        CancellationToken cancellationToken)
    {
        var config = await _repo.GetByIdAsync(
            request.GatewayConfigId,
            includeCredentials: false,
            includeRoutes: true,
            cancellationToken);

        if (config is null)
            return Result<PaymentGatewayConfigDto>.NotFound("Payment gateway config not found.");

        if (request.CountryCodes is not null && request.CountryCodes.Count == 0)
        {
            return Result<PaymentGatewayConfigDto>.Failure(
                "At least one country route is required.",
                400);
        }

        if (request.DisplayName is not null)
        {
            var displayName = request.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                return Result<PaymentGatewayConfigDto>.Failure("DisplayName cannot be empty.", 400);

            config.DisplayName = displayName;
        }

        if (request.LogoUrl is not null)
            config.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();

        if (request.PublicKey is not null)
            config.PublicKey = string.IsNullOrWhiteSpace(request.PublicKey) ? null : request.PublicKey.Trim();

        if (request.MerchantId is not null)
            config.MerchantId = string.IsNullOrWhiteSpace(request.MerchantId) ? null : request.MerchantId.Trim();

        if (request.WebhookUrl is not null)
            config.WebhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();

        if (request.IsActive.HasValue)
            config.IsActive = request.IsActive.Value;

        var now = DateTimeOffset.UtcNow;
        config.UpdatedAt = now;

        if (request.CountryCodes is not null)
        {
            var routeResult = await ReplaceCountryRoutesAsync(
                config,
                request.CountryCodes,
                request.CountryNameSnapshots ?? [],
                request.ActorPlatformUserId,
                now,
                cancellationToken);

            if (!routeResult.IsSuccess)
                return Result<PaymentGatewayConfigDto>.Failure(
                    routeResult.Error!,
                    routeResult.StatusCode ?? 400);
        }

        await _repo.SaveChangesAsync(cancellationToken);

        var activeCredential = await _repo.GetActiveCredentialAsync(config.Id, cancellationToken);
        var routes = await _repo.ListRoutesForConfigAsync(config.Id, cancellationToken);

        return Result<PaymentGatewayConfigDto>.Success(MapToDto(
            config,
            activeCredential?.CredentialVersion ?? -1,
            activeCredential is not null,
            routes));
    }

    private async Task<Result<Unit>> ReplaceCountryRoutesAsync(
        PaymentGatewayConfig config,
        IReadOnlyList<string> countryCodes,
        IReadOnlyList<string?> countryNameSnapshots,
        Guid actorPlatformUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedCountryCodes = new List<string>(countryCodes.Count);
        var distinctCountryCodes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < countryCodes.Count; i++)
        {
            var code = countryCodes[i].ToUpperInvariant();
            if (code.Length != 2 || code.Any(character => character is < 'A' or > 'Z'))
            {
                return Result<Unit>.Failure(
                    $"Country code '{countryCodes[i]}' must be a 2-character ISO 3166-1 alpha-2 code.",
                    400);
            }

            if (!distinctCountryCodes.Add(code))
            {
                return Result<Unit>.Conflict(
                    $"Country code '{code}' is assigned more than once in this request.");
            }

            var hasConflict = await _repo.HasConflictingCountryRouteAsync(
                code,
                config.Environment,
                config.Id,
                cancellationToken);

            if (hasConflict)
            {
                var displayName = countryNameSnapshots.Count > i
                    ? (countryNameSnapshots[i] ?? code)
                    : code;

                return Result<Unit>.Conflict(
                    $"{displayName} ({code}) already has an active gateway route for {config.Environment}. " +
                    "Deactivate the existing route before assigning this country to a new gateway.");
            }

            normalizedCountryCodes.Add(code);
        }

        var requestedCodes = normalizedCountryCodes.ToHashSet(StringComparer.Ordinal);
        var routesForEnvironment = config.CountryRoutes
            .Where(route => route.Environment == config.Environment)
            .ToList();

        var routesByCode = routesForEnvironment
            .GroupBy(route => route.CountryCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var route in routesForEnvironment)
        {
            if (!requestedCodes.Contains(route.CountryCode))
            {
                route.IsActive = false;
                route.UpdatedAt = now;
            }
        }

        for (var i = 0; i < normalizedCountryCodes.Count; i++)
        {
            var code = normalizedCountryCodes[i];
            var snapshot = countryNameSnapshots.Count > i ? countryNameSnapshots[i] : null;

            if (routesByCode.TryGetValue(code, out var existingRoutes))
            {
                var primaryRoute = existingRoutes[0];
                primaryRoute.IsActive = true;
                primaryRoute.CountryNameSnapshot = snapshot;
                primaryRoute.UpdatedAt = now;

                foreach (var duplicateRoute in existingRoutes.Skip(1))
                {
                    duplicateRoute.IsActive = false;
                    duplicateRoute.UpdatedAt = now;
                }

                continue;
            }

            var newRoute = new PaymentGatewayCountryRoute
            {
                Id = Guid.NewGuid(),
                CountryCode = code,
                CountryNameSnapshot = snapshot,
                GatewayConfigId = config.Id,
                Environment = config.Environment,
                IsActive = true,
                CreatedById = actorPlatformUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _repo.AddCountryRouteAsync(newRoute, cancellationToken);
            config.CountryRoutes.Add(newRoute);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static PaymentGatewayConfigDto MapToDto(
        PaymentGatewayConfig config,
        int activeVersion,
        bool hasActiveCredential,
        IReadOnlyList<PaymentGatewayCountryRoute> routes)
        => new()
        {
            Id = config.Id,
            GatewayKey = config.GatewayKey,
            Provider = config.Provider,
            Environment = config.Environment,
            DisplayName = config.DisplayName,
            LogoUrl = config.LogoUrl,
            PublicKey = config.PublicKey,
            MerchantId = config.MerchantId,
            WebhookUrl = config.WebhookUrl,
            IsActive = config.IsActive,
            HasActiveCredential = hasActiveCredential,
            ActiveCredentialVersion = activeVersion,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            CountryRoutes = routes.Select(route => new PaymentGatewayCountryRouteDto
            {
                Id = route.Id,
                CountryCode = route.CountryCode,
                CountryNameSnapshot = route.CountryNameSnapshot,
                Environment = route.Environment,
                IsActive = route.IsActive,
                CreatedAt = route.CreatedAt
            }).ToList()
        };
}
