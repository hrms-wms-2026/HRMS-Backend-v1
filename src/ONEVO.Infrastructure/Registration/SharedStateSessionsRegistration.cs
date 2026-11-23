using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Seeders;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.SharedState;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Shared state for more than one instance (task 21) and the session ticket cache (task 03).</summary>
internal static class SharedStateSessionsRegistration
{
    internal static IServiceCollection AddSharedStateAndSessions(this IServiceCollection services, IConfiguration configuration)
    {
        // Task 21: shared state for more than one instance (Redis when Redis:ConnectionString is set, else in process).
        services.AddSharedState(configuration);
        if (SharedState.SharedStateRegistration.IsRedisEnabled(configuration))
            services.AddSingleton<IPermissionVersionService, SharedState.RedisPermissionVersionService>();
        else
            services.AddSingleton<IPermissionVersionService, PermissionVersionService>();
        services.AddSingleton<SharedState.IInvalidationHandler, SharedState.SessionTicketInvalidationHandler>();
        services.AddSingleton<SharedState.IInvalidationHandler, SharedState.MemoryCacheInvalidationHandler>();
        // Improvement task 03: short-lived cache in front of the ticket stores' per-request lookups.
        services.AddMemoryCache();
        services.Configure<ONEVO.Infrastructure.Identity.Sessions.SessionTicketCacheOptions>(
            configuration.GetSection(ONEVO.Infrastructure.Identity.Sessions.SessionTicketCacheOptions.SectionName));
        services.AddSingleton<ONEVO.Infrastructure.Identity.Sessions.SessionTicketCache>();
        services.AddSingleton<ONEVO.Infrastructure.Identity.Sessions.ISessionTicketStoreCache>(
            sp => sp.GetRequiredService<ONEVO.Infrastructure.Identity.Sessions.SessionTicketCache>());
        // Evictions from callers also reach the other instances (task 21); the ticket stores read the local cache.
        services.AddSingleton<ISessionTicketCache>(sp => new SharedState.BroadcastingSessionTicketCache(
            sp.GetRequiredService<ONEVO.Infrastructure.Identity.Sessions.SessionTicketCache>(),
            sp.GetRequiredService<SharedState.ISharedInvalidationBus>()));

        return services;
    }
}
