using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

/// <summary>
/// EF Core repository implementation for payment gateway config management.
/// Phase 1 canonical tables: payment_gateway_configs, payment_gateway_credentials,
/// payment_gateway_country_routes.
/// SECURITY: This repository never reads or returns secret fields to callers.
/// Credential bytes are written via EncryptionService before reaching here.
/// </summary>
public sealed class EfPaymentGatewayRepository : IPaymentGatewayRepository
{
    private readonly ApplicationDbContext _db;

    public EfPaymentGatewayRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // Config

    public async Task<IReadOnlyList<PaymentGatewayConfig>> ListAllAsync(CancellationToken ct)
    {
        var query = _db.PaymentGatewayConfigs
            .AsNoTracking()
            .OrderBy(g => g.Provider)
            .ThenBy(g => g.DisplayName);

        var configs = await query.ToListAsync(ct);

        return configs;
    }

    public async Task<PaymentGatewayConfig?> GetByIdAsync(
        Guid id,
        bool includeCredentials,
        bool includeRoutes,
        CancellationToken ct)
    {
        IQueryable<PaymentGatewayConfig> query = _db.PaymentGatewayConfigs;

        if (includeCredentials)
        {
            query = query.Include(g => g.Credentials);
        }

        if (includeRoutes)
        {
            query = query.Include(g => g.CountryRoutes);
        }

        var config = await query.FirstOrDefaultAsync(g => g.Id == id, ct);

        return config;
    }

    public async Task<PaymentGatewayConfig?> GetByGatewayKeyAsync(
        string gatewayKey,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayConfigs
            .Where(config => config.GatewayKey == gatewayKey);

        var config = await query.FirstOrDefaultAsync(ct);

        return config;
    }

    public async Task<bool> HasConflictingCountryRouteAsync(
        string countryCode,
        string environment,
        Guid excludeGatewayConfigId,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayCountryRoutes
            .Where(route => route.CountryCode == countryCode)
            .Where(route => route.Environment == environment)
            .Where(route => route.IsActive)
            .Where(route => route.GatewayConfigId != excludeGatewayConfigId);

        var exists = await query.AnyAsync(ct);

        return exists;
    }

    public async Task AddAsync(PaymentGatewayConfig config, CancellationToken ct)
    {
        await _db.PaymentGatewayConfigs.AddAsync(config, ct);
    }

    public async Task<PaymentGatewayConfig?> ResolveForCountryAsync(
        string countryCode,
        string environment,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayCountryRoutes
            .AsNoTracking()
            .Include(r => r.GatewayConfig)
            .Where(route => route.CountryCode == countryCode)
            .Where(route => route.Environment == environment)
            .Where(route => route.IsActive)
            .Where(route => route.GatewayConfig!.IsActive);

        var route = await query.FirstOrDefaultAsync(ct);

        return route?.GatewayConfig;
    }

    // Credentials

    public async Task<PaymentGatewayCredential?> GetActiveCredentialAsync(
        Guid gatewayConfigId,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayCredentials
            .Where(credential => credential.PaymentGatewayConfigId == gatewayConfigId)
            .Where(credential => credential.IsActive);

        var credential = await query.FirstOrDefaultAsync(ct);

        return credential;
    }

    public async Task<IReadOnlyList<PaymentGatewayCredential>> GetActiveCredentialsAsync(
        Guid gatewayConfigId,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayCredentials
            .Where(credential => credential.PaymentGatewayConfigId == gatewayConfigId)
            .Where(credential => credential.IsActive);

        var credentials = await query.ToListAsync(ct);

        return credentials;
    }

    public async Task<int> GetMaxCredentialVersionAsync(Guid gatewayConfigId, CancellationToken ct)
    {
        var query = _db.PaymentGatewayCredentials
            .Where(credential => credential.PaymentGatewayConfigId == gatewayConfigId);

        var maxCredentialVersion = await query
            .MaxAsync(credential => (int?)credential.CredentialVersion, ct);

        return maxCredentialVersion ?? 0;
    }

    public async Task AddCredentialAsync(PaymentGatewayCredential credential, CancellationToken ct)
    {
        await _db.PaymentGatewayCredentials.AddAsync(credential, ct);
    }

    // Country Routes

    public async Task<IReadOnlyList<PaymentGatewayCountryRoute>> ListRoutesForConfigAsync(
        Guid gatewayConfigId,
        CancellationToken ct)
    {
        var query = _db.PaymentGatewayCountryRoutes
            .AsNoTracking()
            .Where(route => route.GatewayConfigId == gatewayConfigId)
            .OrderBy(route => route.CountryCode);

        var routes = await query.ToListAsync(ct);

        return routes;
    }

    public async Task AddCountryRouteAsync(PaymentGatewayCountryRoute route, CancellationToken ct)
    {
        await _db.PaymentGatewayCountryRoutes.AddAsync(route, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}
