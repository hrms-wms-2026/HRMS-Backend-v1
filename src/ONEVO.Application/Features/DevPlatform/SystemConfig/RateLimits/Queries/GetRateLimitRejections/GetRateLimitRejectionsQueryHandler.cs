using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitRejections;

/// <summary>
/// Reads the in-memory counts, keeps the top 50 tenants, and adds each tenant's name (one query)
/// and its effective per-tenant limits (from the in-memory limit provider, no database read).
/// </summary>
public sealed class GetRateLimitRejectionsQueryHandler
    : IRequestHandler<GetRateLimitRejectionsQuery, Result<RateLimitRejectionsDto>>
{
    public const string InstanceScope = "instance";

    private static readonly RateLimitName[] PerTenantLimits =
        [RateLimitName.SignedIn, RateLimitName.TrayDevice, RateLimitName.TenantCeiling];

    private readonly IRateLimitRejectionStats _stats;
    private readonly IRateLimitSettingsProvider _settings;
    private readonly IRateLimitSettingRepository _repository;

    public GetRateLimitRejectionsQueryHandler(
        IRateLimitRejectionStats stats,
        IRateLimitSettingsProvider settings,
        IRateLimitSettingRepository repository)
    {
        _stats = stats;
        _settings = settings;
        _repository = repository;
    }

    public async Task<Result<RateLimitRejectionsDto>> Handle(GetRateLimitRejectionsQuery request, CancellationToken ct)
    {
        var snapshot = _stats.Read(request.Minutes);

        var top = snapshot.Counts
            .GroupBy(c => c.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Sum(c => c.Count),
                ByKind = g.OrderByDescending(c => c.Count)
                    .ThenBy(c => c.Kind, StringComparer.Ordinal)
                    .Select(c => new RateLimitRejectionKindDto(c.Kind, c.Count))
                    .ToList(),
            })
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.TenantId)
            .Take(GetRateLimitRejectionsQuery.MaxTenants)
            .ToList();

        var tenantIds = top.Where(t => t.TenantId is not null).Select(t => t.TenantId!.Value).ToList();
        var names = tenantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _repository.GetTenantNamesAsync(tenantIds, ct);

        var tenants = top
            .Select(t => new TenantRateLimitRejectionsDto(
                t.TenantId,
                t.TenantId is Guid id ? names.GetValueOrDefault(id) : null,
                t.Total,
                t.ByKind,
                t.TenantId is Guid tenantId ? EffectiveLimits(tenantId) : []))
            .ToList();

        return Result<RateLimitRejectionsDto>.Success(new RateLimitRejectionsDto(
            InstanceScope,
            $"Counts from this API server only, for the last {request.Minutes} minutes, since it last started. "
                + "Other API servers keep their own counts until shared counters are added (task 21).",
            request.Minutes,
            snapshot.GeneratedAt,
            snapshot.CountingSince,
            snapshot.Counts.Sum(c => c.Count),
            tenants));
    }

    private IReadOnlyList<RateLimitRejectionLimitDto> EffectiveLimits(Guid tenantId)
    {
        var settings = _settings.GetSettings(tenantId).Settings;
        return PerTenantLimits
            .Select(name =>
            {
                var value = RateLimitResolver.FromAppSettings(settings, name);
                return new RateLimitRejectionLimitDto(name.ToStoredName(), value.PerMinute, value.Burst);
            })
            .ToList();
    }
}
