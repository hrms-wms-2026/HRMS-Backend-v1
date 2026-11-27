using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure;

/// <summary>
/// Registers everything the Infrastructure layer provides. Each area has its own method in
/// <c>Registration/</c>; add a new registration to the file of its area (docs/development/architecture-rules.md).
/// <para>
/// The call order matters only where several registrations share one service type (inbox plug-ins, outbox
/// handlers, team action sources, options validation, hosted services): DI keeps them in registration order, and
/// hosted services start in that order. The order below keeps every such list exactly as it was before the split
/// (task 25), and the seeders run last inside their lock.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddDatabase(configuration)
            .AddIdentityAndAccess(configuration)
            .AddDeveloperPlatform(configuration)
            .AddOrgStructure(configuration)
            .AddTimeAttendance(configuration)
            .AddLeave(configuration)
            .AddMonitoring(configuration)
            .AddCoreHr(configuration)
            .AddInbox(configuration)
            .AddWorkManagement(configuration)
            .AddStorage(configuration)
            .AddCalendarAndIntegrations(configuration)
            .AddMessaging(configuration)
            .AddRequestContext(configuration)
            .AddDashboard(configuration)
            .AddRecurringJobs(configuration)
            .AddSharedStateAndSessions(configuration)
            .AddSeeders(configuration);

        // Task 46: rate limits with the database overrides, registered before AddApiRateLimiting runs.
        Services.SharedPlatform.RateLimiting.RateLimitSettingsRegistration.AddDatabaseRateLimitSettings(services);

        // Boot-time configuration audit (warns about missing keys; never fatal). Last hosted service.
        services.AddHostedService<ConfigurationStartupValidator>();

        return services;
    }
}
