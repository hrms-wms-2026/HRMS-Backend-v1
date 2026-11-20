using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

public static class RateLimitSettingsRegistration
{
    /// <summary>
    /// Task 46: the database-backed provider replaces the appsettings one. It is registered before
    /// AddApiRateLimiting, whose TryAddSingleton then leaves it in place. The appsettings provider
    /// stays as the base and fallback.
    /// </summary>
    public static IServiceCollection AddDatabaseRateLimitSettings(this IServiceCollection services)
    {
        services.AddSingleton<OptionsRateLimitSettingsProvider>();
        services.AddSingleton<IRateLimitOverrideLoader, EfRateLimitOverrideLoader>();
        services.AddSingleton<DatabaseRateLimitSettingsProvider>();
        services.AddSingleton<IRateLimitSettingsProvider>(sp => sp.GetRequiredService<DatabaseRateLimitSettingsProvider>());
        services.AddSingleton<IRateLimitSettingsInvalidator>(sp => sp.GetRequiredService<DatabaseRateLimitSettingsProvider>());
        services.AddHostedService<RateLimitSettingsRefreshService>();
        return services;
    }
}
