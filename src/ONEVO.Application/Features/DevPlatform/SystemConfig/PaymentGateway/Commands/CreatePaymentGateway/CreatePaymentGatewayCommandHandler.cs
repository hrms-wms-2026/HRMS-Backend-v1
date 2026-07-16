using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;

/// <summary>
/// Creates a payment gateway config with its first encrypted credential version and country routes.
/// Credential secrets are encrypted before persistence and never returned by any GET response.
/// </summary>
public sealed record CreatePaymentGatewayCommand(
    string GatewayKey,
    string Provider,
    string Environment,
    string DisplayName,
    string? LogoUrl,
    string? PublicKey,
    string? MerchantId,
    string? WebhookUrl,
    bool IsActive,
    /// <summary>Plain-text secret — encrypted immediately; never stored raw.</summary>
    string SecretKey,
    /// <summary>Plain-text webhook secret — encrypted immediately; never stored raw. Nullable.</summary>
    string? WebhookSecret,
    IReadOnlyList<string> CountryCodes,
    IReadOnlyList<string?> CountryNameSnapshots,
    Guid ActorPlatformUserId) : IRequest<Result<PaymentGatewayConfigDto>>;

public sealed class CreatePaymentGatewayCommandHandler
    : IRequestHandler<CreatePaymentGatewayCommand, Result<PaymentGatewayConfigDto>>
{
    private readonly IPaymentGatewayRepository _repo;
    private readonly IEncryptionService _encryption;

    public CreatePaymentGatewayCommandHandler(
        IPaymentGatewayRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<Result<PaymentGatewayConfigDto>> Handle(
        CreatePaymentGatewayCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate provider discriminator
        if (!IsValidProvider(request.Provider))
            return Result<PaymentGatewayConfigDto>.Failure(
                $"Provider must be one of: stripe, paddle, payhere. Got: '{request.Provider}'", 400);

        // 2. Validate environment
        if (!IsValidEnvironment(request.Environment))
            return Result<PaymentGatewayConfigDto>.Failure(
                "Environment must be 'sandbox' or 'production'.", 400);

        // 3. Check gateway_key uniqueness
        var existing = await _repo.GetByGatewayKeyAsync(request.GatewayKey, cancellationToken);
        if (existing is not null)
            return Result<PaymentGatewayConfigDto>.Conflict(
                $"A gateway config with key '{request.GatewayKey}' already exists.");

        // 4. Validate country routes — check for per-country conflicts
        for (var i = 0; i < request.CountryCodes.Count; i++)
        {
            var code = request.CountryCodes[i].ToUpperInvariant();
            if (code.Length != 2)
                return Result<PaymentGatewayConfigDto>.Failure(
                    $"Country code '{code}' must be a 2-character ISO 3166-1 alpha-2 code.", 400);

            // For create, excludeGatewayConfigId = Guid.Empty so all existing routes are checked
            var hasConflict = await _repo.HasConflictingCountryRouteAsync(
                code, request.Environment, Guid.Empty, cancellationToken);

            if (hasConflict)
            {
                var displayName = request.CountryNameSnapshots.Count > i
                    ? (request.CountryNameSnapshots[i] ?? code)
                    : code;
                return Result<PaymentGatewayConfigDto>.Conflict(
                    $"{displayName} ({code}) already has an active gateway route for {request.Environment}. " +
                    "Deactivate the existing route before assigning this country to a new gateway.");
            }
        }

        // 5. Encrypt credentials — NEVER stored plaintext
        var secretEncrypted = _encryption.EncryptBytes(request.SecretKey);
        byte[]? webhookSecretEncrypted = request.WebhookSecret is not null
            ? _encryption.EncryptBytes(request.WebhookSecret)
            : null;

        // 6. Build config entity
        var configId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var config = new PaymentGatewayConfig
        {
            Id = configId,
            GatewayKey = request.GatewayKey,
            Provider = request.Provider,
            Environment = request.Environment,
            DisplayName = request.DisplayName,
            LogoUrl = request.LogoUrl,
            PublicKey = request.PublicKey,
            MerchantId = request.MerchantId,
            WebhookUrl = request.WebhookUrl,
            IsActive = request.IsActive,
            CreatedById = request.ActorPlatformUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        // 7. Build first credential version
        var credential = new PaymentGatewayCredential
        {
            Id = Guid.NewGuid(),
            PaymentGatewayConfigId = configId,
            SecretEncrypted = secretEncrypted,
            WebhookSecretEncrypted = webhookSecretEncrypted,
            EncryptionKeyVersion = "v1",
            CredentialVersion = 1,
            IsActive = true,
            RotatedById = request.ActorPlatformUserId,
            RotatedAt = now
        };

        // 8. Build country route rows
        var routes = new List<PaymentGatewayCountryRoute>();
        for (var i = 0; i < request.CountryCodes.Count; i++)
        {
            routes.Add(new PaymentGatewayCountryRoute
            {
                Id = Guid.NewGuid(),
                CountryCode = request.CountryCodes[i].ToUpperInvariant(),
                CountryNameSnapshot = request.CountryNameSnapshots.Count > i
                    ? request.CountryNameSnapshots[i]
                    : null,
                GatewayConfigId = configId,
                Environment = request.Environment,
                IsActive = true,
                CreatedById = request.ActorPlatformUserId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // 9. Persist — repo SaveChanges handles atomicity
        await _repo.AddAsync(config, cancellationToken);
        await _repo.AddCredentialAsync(credential, cancellationToken);
        foreach (var route in routes)
            await _repo.AddCountryRouteAsync(route, cancellationToken);

        await _repo.SaveChangesAsync(cancellationToken);

        return Result<PaymentGatewayConfigDto>.Success(new PaymentGatewayConfigDto
        {
            Id = configId,
            GatewayKey = config.GatewayKey,
            Provider = config.Provider,
            Environment = config.Environment,
            DisplayName = config.DisplayName,
            LogoUrl = config.LogoUrl,
            PublicKey = config.PublicKey,
            MerchantId = config.MerchantId,
            WebhookUrl = config.WebhookUrl,
            IsActive = config.IsActive,
            HasActiveCredential = true,
            ActiveCredentialVersion = 1,
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
        });
    }

    private static bool IsValidProvider(string provider) =>
        provider is "stripe" or "paddle" or "payhere";

    private static bool IsValidEnvironment(string environment) =>
        environment is "sandbox" or "production";
}
