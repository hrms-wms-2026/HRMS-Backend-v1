using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Services.SharedPlatform;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Tenant context, current user, cache and clock.</summary>
internal static class RequestContextRegistration
{
    internal static IServiceCollection AddRequestContext(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<ICurrentPlatformUserContext, CurrentPlatformUserContext>();
        services.AddSingleton<MemoryCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<MemoryCacheService>());
        // Task 22: tenant-keyed reference-data cache; Caching:ReferenceData:Enabled=false sends every read to the DB.
        services.AddOptions<ReferenceDataCacheOptions>()
            .Bind(configuration.GetSection(ReferenceDataCacheOptions.SectionName))
            .Validate(options => options.LifetimeMinutes > 0, "Caching:ReferenceData:LifetimeMinutes must be positive.")
            .ValidateOnStart();
        services.AddScoped<ITenantCache, TenantCache>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}
