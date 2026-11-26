using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

/// <summary>Where an override lives: the platform, one plan or one tenant.</summary>
public sealed record RateLimitTarget(string Scope, Guid? SubscriptionPlanId, Guid? TenantId)
{
    public static RateLimitTarget Global { get; } = new(RateLimitSettingRules.GlobalScope, null, null);

    public static RateLimitTarget Plan(Guid planId) => new(RateLimitSettingRules.PlanScope, planId, null);

    public static RateLimitTarget Tenant(Guid tenantId) => new(RateLimitSettingRules.TenantScope, null, tenantId);
}

/// <summary>The value an admin asked for: null means "remove the override".</summary>
public sealed record RateLimitChangeRequest(
    RateLimitTarget Target,
    RateLimitName LimitName,
    RateLimitValue? NewValue,
    string Reason,
    string? RowVersion,
    string? CorrelationId);

/// <summary>
/// Reads and writes rate limit overrides for the admin API (Improvements task 47). Every write saves
/// the override and one history row in a single SaveChanges (one transaction), then asks for a reload
/// of the task 46 provider (<see cref="RateLimitReloadSignal"/>). <see cref="RateLimitReloadBehavior{TRequest, TResponse}"/>
/// runs it after the transaction commits, so the change applies on this instance at once. A no-op
/// writes nothing and asks for nothing.
/// </summary>
public sealed class RateLimitAdminService
{
    private const int CorrelationIdMaxLength = 100;

    private readonly IRateLimitSettingRepository _repository;
    private readonly RateLimitReloadSignal _reload;
    private readonly IOptionsMonitor<RateLimitSettings> _appSettings;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly IDateTimeProvider _clock;

    public RateLimitAdminService(
        IRateLimitSettingRepository repository,
        RateLimitReloadSignal reload,
        IOptionsMonitor<RateLimitSettings> appSettings,
        ICurrentPlatformUserContext platformUser,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _reload = reload;
        _appSettings = appSettings;
        _platformUser = platformUser;
        _clock = clock;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<GlobalRateLimitDto>> GetGlobalAsync(CancellationToken ct)
    {
        var appSettings = _appSettings.CurrentValue;
        var rows = await _repository.ListForScopeAsync(null, null, ct);

        return RateLimitNames.All.Select(name =>
        {
            var row = Find(rows, RateLimitTarget.Global, name);
            var effective = RateLimitResolver.Resolve(name, appSettings, rows, null, null);
            return new GlobalRateLimitDto(
                name.ToStoredName(),
                Algorithm(name),
                ToOverride(row),
                ToDto(RateLimitResolver.FromAppSettings(appSettings, name)),
                ToDto(effective.Value),
                effective.Source,
                ToBounds(name));
        }).ToList();
    }

    public async Task<Result<PlanRateLimitsDto>> GetPlanAsync(Guid planId, CancellationToken ct)
    {
        if (await _repository.GetPlanAsync(planId, ct) is null)
            return Result<PlanRateLimitsDto>.NotFound($"Subscription plan '{planId}' not found.");

        return Result<PlanRateLimitsDto>.Success(await BuildPlanAsync(planId, ct));
    }

    /// <summary>The plan view, for a plan already known to exist.</summary>
    public async Task<PlanRateLimitsDto> BuildPlanAsync(Guid planId, CancellationToken ct)
    {
        var appSettings = _appSettings.CurrentValue;
        var rows = await _repository.ListForScopeAsync(planId, null, ct);
        var target = RateLimitTarget.Plan(planId);

        var limits = RateLimitNames.PerTenant.Select(name =>
        {
            var inherited = RateLimitResolver.Resolve(name, appSettings, rows, null, null);
            var effective = RateLimitResolver.Resolve(name, appSettings, rows, planId, null);
            return new PlanRateLimitDto(
                name.ToStoredName(),
                Algorithm(name),
                ToOverride(Find(rows, target, name)),
                ToDto(inherited.Value),
                inherited.Source,
                ToDto(effective.Value),
                effective.Source,
                ToBounds(name));
        }).ToList();

        return new PlanRateLimitsDto(planId, limits);
    }

    public async Task<Result<TenantRateLimitsDto>> GetTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _repository.GetTenantAsync(tenantId, ct);
        if (tenant is null)
            return Result<TenantRateLimitsDto>.NotFound($"Tenant '{tenantId}' not found.");

        var appSettings = _appSettings.CurrentValue;
        var rows = await _repository.ListForScopeAsync(tenant.SubscriptionPlanId, tenantId, ct);
        var target = RateLimitTarget.Tenant(tenantId);

        var limits = RateLimitNames.PerTenant.Select(name =>
        {
            var effective = RateLimitResolver.Resolve(name, appSettings, rows, tenant.SubscriptionPlanId, tenantId);
            return new TenantRateLimitDto(
                name.ToStoredName(),
                Algorithm(name),
                ToOverride(Find(rows, target, name)),
                ToDto(effective.Value),
                effective.Source,
                ToBounds(name));
        }).ToList();

        return Result<TenantRateLimitsDto>.Success(new TenantRateLimitsDto(tenantId, tenant.SubscriptionPlanId, limits));
    }

    // ---------------------------------------------------------------- writes

    /// <summary>Sets or removes one override. Returns 404, 400 or 409 failures as described in task 47.</summary>
    public async Task<Result> ApplyAsync(RateLimitChangeRequest request, CancellationToken ct)
    {
        var actor = _platformUser.UserId;
        if (actor is null)
            return Result.Forbidden("A platform user is required.");

        var target = request.Target;
        var limitName = request.LimitName;
        if (target.Scope != RateLimitSettingRules.GlobalScope && !limitName.IsPerTenant())
            return Result.Failure(
                $"'{limitName}' is platform-wide and cannot be set on a plan or tenant.", 400, "rate_limit_platform_only");

        // The plan whose rows a tenant inherits; null for global and plan targets.
        Guid? tenantPlanId = null;
        if (target.SubscriptionPlanId is Guid planId)
        {
            var plan = await _repository.GetPlanAsync(planId, ct);
            if (plan is null)
                return Result.NotFound($"Subscription plan '{planId}' not found.");
            if (!plan.IsActive)
                return Result.Failure("The subscription plan is archived.", 400, "rate_limit_plan_archived");
        }
        else if (target.TenantId is Guid tenantId)
        {
            var tenant = await _repository.GetTenantAsync(tenantId, ct);
            if (tenant is null)
                return Result.NotFound($"Tenant '{tenantId}' not found.");
            if (tenant.IsArchived)
                return Result.Failure("The tenant is archived.", 400, "rate_limit_tenant_archived");
            tenantPlanId = tenant.SubscriptionPlanId;
        }

        var storedName = limitName.ToStoredName();
        var existing = await _repository.GetTrackedAsync(
            target.Scope, target.SubscriptionPlanId, target.TenantId, storedName, ct);
        var newValue = request.NewValue;

        // A no-op writes no history and does not reload.
        if (existing is null && newValue is null)
            return Result.Success();
        if (existing is not null && newValue is not null
            && existing.PerMinute == newValue.PerMinute && existing.Burst == newValue.Burst)
            return Result.Success();

        var conflict = CheckVersion(existing, request.RowVersion);
        if (conflict is not null)
            return conflict;

        var ceiling = await CheckCeilingAsync(target, tenantPlanId, limitName, newValue, ct);
        if (ceiling is not null)
            return ceiling;

        var now = _clock.UtcNow;
        _repository.AddChange(new RateLimitSettingChange
        {
            Id = Guid.NewGuid(),
            ChangedAt = now,
            PlatformUserId = actor.Value,
            Scope = target.Scope,
            SubscriptionPlanId = target.SubscriptionPlanId,
            TenantId = target.TenantId,
            LimitName = storedName,
            OldPerMinute = existing?.PerMinute,
            OldBurst = existing?.Burst,
            NewPerMinute = newValue?.PerMinute,
            NewBurst = newValue?.Burst,
            Reason = request.Reason.Trim(),
            CorrelationId = Truncate(request.CorrelationId),
        });

        if (newValue is null)
        {
            _repository.Remove(existing!);
        }
        else if (existing is null)
        {
            _repository.Add(new RateLimitSetting
            {
                Id = Guid.NewGuid(),
                Scope = target.Scope,
                SubscriptionPlanId = target.SubscriptionPlanId,
                TenantId = target.TenantId,
                LimitName = storedName,
                PerMinute = newValue.PerMinute,
                Burst = newValue.Burst,
                UpdatedAt = now,
                UpdatedByPlatformUserId = actor.Value,
            });
        }
        else
        {
            existing.PerMinute = newValue.PerMinute;
            existing.Burst = newValue.Burst;
            existing.UpdatedAt = now;
            existing.UpdatedByPlatformUserId = actor.Value;
        }

        try
        {
            await _repository.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return StaleConflict();
        }
        catch (UniqueConstraintConflictException)
        {
            return StaleConflict();
        }

        // The reload itself runs in RateLimitReloadBehavior, after the transaction commits.
        _reload.Request();
        return Result.Success();
    }

    private Result? CheckVersion(RateLimitSetting? existing, string? rowVersion)
    {
        var hasToken = !string.IsNullOrWhiteSpace(rowVersion);
        if (existing is null)
            // The caller saw a row that is gone now.
            return hasToken ? StaleConflict() : null;

        // The caller did not see the row another admin created, or sent an old token.
        if (!hasToken)
            return StaleConflict();
        if (!_repository.SetExpectedVersion(existing, rowVersion!))
            return Result.Failure("rowVersion is not valid.", 400, "rate_limit_row_version_invalid");
        return null;
    }

    /// <summary>
    /// TenantCeiling must be at least the effective SignedIn limit in the same scope, for both
    /// numbers, or the ceiling would cut off a single user before their own limit does. Checked on
    /// the result of the change, so it covers saving and removing either limit.
    /// </summary>
    private async Task<Result?> CheckCeilingAsync(
        RateLimitTarget target, Guid? tenantPlanId, RateLimitName limitName, RateLimitValue? newValue, CancellationToken ct)
    {
        if (limitName is not (RateLimitName.SignedIn or RateLimitName.TenantCeiling))
            return null;

        var planId = target.SubscriptionPlanId ?? tenantPlanId;
        var rows = (await _repository.ListForScopeAsync(planId, target.TenantId, ct)).ToList();
        var storedName = limitName.ToStoredName();
        rows.RemoveAll(r => r.Scope == target.Scope && r.LimitName == storedName
            && r.SubscriptionPlanId == target.SubscriptionPlanId && r.TenantId == target.TenantId);
        if (newValue is not null)
        {
            rows.Add(new RateLimitOverrideRecord(
                Guid.Empty, target.Scope, target.SubscriptionPlanId, target.TenantId, storedName,
                newValue.PerMinute, newValue.Burst, DateTimeOffset.MinValue, Guid.Empty, string.Empty));
        }

        var appSettings = _appSettings.CurrentValue;
        var signedIn = RateLimitResolver.Resolve(RateLimitName.SignedIn, appSettings, rows, planId, target.TenantId).Value;
        var ceiling = RateLimitResolver.Resolve(RateLimitName.TenantCeiling, appSettings, rows, planId, target.TenantId).Value;

        if (ceiling.PerMinute >= signedIn.PerMinute && (ceiling.Burst ?? 0) >= (signedIn.Burst ?? 0))
            return null;

        return Result.Failure(
            $"TenantCeiling ({ceiling.PerMinute}/min, burst {ceiling.Burst}) must be at least the SignedIn limit "
            + $"({signedIn.PerMinute}/min, burst {signedIn.Burst}) for the same {target.Scope}.",
            400,
            "rate_limit_ceiling_below_signed_in");
    }

    private static Result StaleConflict() => Result.Failure(
        "The rate limit was changed by someone else. Reload and try again.", 409, "rate_limit_conflict");

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= CorrelationIdMaxLength ? value : value[..CorrelationIdMaxLength];

    private static RateLimitOverrideRecord? Find(
        IEnumerable<RateLimitOverrideRecord> rows, RateLimitTarget target, RateLimitName name) =>
        rows.FirstOrDefault(r => r.Scope == target.Scope && r.LimitName == name.ToStoredName()
            && r.SubscriptionPlanId == target.SubscriptionPlanId && r.TenantId == target.TenantId);

    private static string Algorithm(RateLimitName name) =>
        name.IsSlidingWindow() ? RateLimitAlgorithms.SlidingWindow : RateLimitAlgorithms.TokenBucket;

    private static RateLimitOverrideDto? ToOverride(RateLimitOverrideRecord? row) =>
        row is null ? null : new(row.PerMinute, row.Burst, row.UpdatedAt, row.UpdatedByPlatformUserId, row.RowVersion);

    private static RateLimitValueDto ToDto(RateLimitValue value) => new(value.PerMinute, value.Burst);

    private static RateLimitBoundsDto ToBounds(RateLimitName name)
    {
        var bounds = name.Bounds();
        return new(bounds.MinPerMinute, bounds.MaxPerMinute, bounds.MinBurst, bounds.MaxBurst);
    }
}
