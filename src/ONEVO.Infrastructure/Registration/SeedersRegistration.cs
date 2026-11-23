using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Reference and demo seeders inside the seeding lock, in the order they run.</summary>
internal static class SeedersRegistration
{
    internal static IServiceCollection AddSeeders(this IServiceCollection services, IConfiguration configuration)
    {
        // Background seeder. Hosted services start one at a time in this order: the seeding lock (task 13) makes a
        // second instance that starts at the same time wait until the first has seeded, instead of inserting twice.
        services.AddSingleton<ReferenceSeedingLock>();
        services.AddHostedService<ReferenceSeedingLock.Acquire>();
        services.AddHostedService<PermissionSeeder>();
        services.AddHostedService<RoleTemplateSeeder>();
        services.AddHostedService<LookupDataSeeder>();
        services.AddHostedService<NotificationTemplateSeeder>();
        services.AddBackgroundJob<Services.SharedPlatform.Notifications.NotificationTrayExpiryJob>(configuration);
        services.AddHostedService<PlatformAccessSeeder>();
        services.AddHostedService<PositionTemplatePackSeeder>();
        services.AddHostedService<ModuleCatalogSeeder>();
        services.AddHostedService<DevSmokeTestTenantSeeder>();
        services.AddHostedService<WorkManagementDapiDemoSeeder>();
        services.AddHostedService<DapiOrgStructureSeeder>();
        services.AddHostedService<DapiLeaveSampleSeeder>();
        services.AddHostedService<DapiAttendanceDemoSeeder>();
        services.AddHostedService<DapiHistorySeeder>();
        services.AddHostedService<PlatformOAuthProviderMetadataSeeder>();
        services.AddHostedService<WorkManagementSampleDataSeeder>();
        services.AddHostedService<ReferenceSeedingLock.Release>();

        return services;
    }
}
