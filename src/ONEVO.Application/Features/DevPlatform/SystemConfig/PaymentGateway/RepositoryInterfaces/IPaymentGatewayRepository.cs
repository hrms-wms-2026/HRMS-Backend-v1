using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;

/// <summary>
/// Data access contract for System Config payment gateway management.
/// Phase 1 canonical tables: payment_gateway_configs, payment_gateway_credentials,
/// payment_gateway_country_routes.
/// </summary>
public interface IPaymentGatewayRepository
{
    // Config CRUD

    Task<IReadOnlyList<PaymentGatewayConfig>> ListAllAsync(CancellationToken ct);

    Task<PaymentGatewayConfig?> GetByIdAsync(Guid id, bool includeCredentials, bool includeRoutes, CancellationToken ct);

    Task<PaymentGatewayConfig?> GetByGatewayKeyAsync(string gatewayKey, CancellationToken ct);

    /// <summary>
    /// Returns true when an active country route for the given country + environment
    /// already exists under a DIFFERENT gateway config - used to enforce "one country,
    /// one active gateway per environment" conflict check before save.
    /// </summary>
    Task<bool> HasConflictingCountryRouteAsync(
        string countryCode,
        string environment,
        Guid excludeGatewayConfigId,
        CancellationToken ct);

    Task AddAsync(PaymentGatewayConfig config, CancellationToken ct);

    /// <summary>
    /// Resolves the active gateway config for a given country + environment.
    /// Used during tenant provisioning Step 3 to assign the correct payment gateway.
    /// Returns null when no active route exists.
    /// </summary>
    Task<PaymentGatewayConfig?> ResolveForCountryAsync(string countryCode, string environment, CancellationToken ct);

    // Credentials

    /// <summary>
    /// Returns the current active (non-deactivated) credential row for a gateway config.
    /// Returns null when no active credential exists.
    /// </summary>
    Task<PaymentGatewayCredential?> GetActiveCredentialAsync(Guid gatewayConfigId, CancellationToken ct);

    /// <summary>
    /// Returns all active credential rows for a gateway config so rotation can
    /// repair an already-inconsistent state before adding the next version.
    /// </summary>
    Task<IReadOnlyList<PaymentGatewayCredential>> GetActiveCredentialsAsync(
        Guid gatewayConfigId,
        CancellationToken ct);

    /// <summary>
    /// Returns the highest credential_version for a gateway config (0 if none).
    /// Used when calculating the next monotonic version number.
    /// </summary>
    Task<int> GetMaxCredentialVersionAsync(Guid gatewayConfigId, CancellationToken ct);

    Task AddCredentialAsync(PaymentGatewayCredential credential, CancellationToken ct);

    // Country Routes

    /// <summary>Lists all active routes for a gateway config.</summary>
    Task<IReadOnlyList<PaymentGatewayCountryRoute>> ListRoutesForConfigAsync(Guid gatewayConfigId, CancellationToken ct);

    Task AddCountryRouteAsync(PaymentGatewayCountryRoute route, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
