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

    public EfPaymentGatewayRepository(ApplicationDbContext db) => _db = db;

    // ── Config ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaymentGatewayConfig>> ListAllAsync(CancellationToken ct)
        => await _db.PaymentGatewayConfigs
            .AsNoTracking()
            .OrderBy(g => g.Provider)
            .ThenBy(g => g.DisplayName)
            .ToListAsync(ct);

    public Task<PaymentGatewayConfig?> GetByIdAsync(
        Guid id,
        bool includeCredentials,
        bool includeRoutes,
        CancellationToken ct)
    {
        IQueryable<PaymentGatewayConfig> q = _db.PaymentGatewayConfigs;
        if (includeCredentials) q = q.Include(g => g.Credentials);
        if (includeRoutes) q = q.Include(g => g.CountryRoutes);
        return q.FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public Task<PaymentGatewayConfig?> GetByGatewayKeyAsync(string gatewayKey, CancellationToken ct)
        => _db.PaymentGatewayConfigs.FirstOrDefaultAsync(g => g.GatewayKey == gatewayKey, ct);

    public Task<bool> HasConflictingCountryRouteAsync(
        string countryCode,
        string environment,
        Guid excludeGatewayConfigId,
        CancellationToken ct)
        => _db.PaymentGatewayCountryRoutes.AnyAsync(
            r => r.CountryCode == countryCode
                 && r.Environment == environment
                 && r.IsActive
                 && r.GatewayConfigId != excludeGatewayConfigId,
            ct);

    public async Task AddAsync(PaymentGatewayConfig config, CancellationToken ct)
        => await _db.PaymentGatewayConfigs.AddAsync(config, ct);

    public async Task<PaymentGatewayConfig?> ResolveForCountryAsync(
        string countryCode,
        string environment,
        CancellationToken ct)
    {
        var route = await _db.PaymentGatewayCountryRoutes
            .AsNoTracking()
            .Include(r => r.GatewayConfig)
            .FirstOrDefaultAsync(
                r => r.CountryCode == countryCode
                     && r.Environment == environment
                     && r.IsActive
                     && r.GatewayConfig!.IsActive,
                ct);

        return route?.GatewayConfig;
    }

    // ── Credentials ──────────────────────────────────────────────────────────

    public Task<PaymentGatewayCredential?> GetActiveCredentialAsync(Guid gatewayConfigId, CancellationToken ct)
        => _db.PaymentGatewayCredentials.FirstOrDefaultAsync(
            c => c.PaymentGatewayConfigId == gatewayConfigId && c.IsActive, ct);

    public async Task<int> GetMaxCredentialVersionAsync(Guid gatewayConfigId, CancellationToken ct)
    {
        var max = await _db.PaymentGatewayCredentials
            .Where(c => c.PaymentGatewayConfigId == gatewayConfigId)
            .MaxAsync(c => (int?)c.CredentialVersion, ct);
        return max ?? 0;
    }

    public async Task AddCredentialAsync(PaymentGatewayCredential credential, CancellationToken ct)
        => await _db.PaymentGatewayCredentials.AddAsync(credential, ct);

    // ── Country Routes ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaymentGatewayCountryRoute>> ListRoutesForConfigAsync(
        Guid gatewayConfigId,
        CancellationToken ct)
        => await _db.PaymentGatewayCountryRoutes
            .AsNoTracking()
            .Where(r => r.GatewayConfigId == gatewayConfigId)
            .OrderBy(r => r.CountryCode)
            .ToListAsync(ct);

    public async Task AddCountryRouteAsync(PaymentGatewayCountryRoute route, CancellationToken ct)
        => await _db.PaymentGatewayCountryRoutes.AddAsync(route, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
