using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Start-up hook for PermissionCatalogSynchronizer: writes <see cref="PermissionCatalog.Seeded"/> and applies
/// <see cref="PermissionCatalog.Lifecycle"/> (renames, default grants, retirements). The run is idempotent and
/// serialized across instances by an app lock, so running it at every start is safe. Set
/// PermissionCatalog:SyncOnStartup=false to leave it to the deploy step (task 09).
/// </summary>
public class PermissionSeeder : IHostedService
{
    public const string SyncOnStartupKey = "PermissionCatalog:SyncOnStartup";

    private readonly IServiceProvider _services;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(IServiceProvider services, ILogger<PermissionSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = _services.GetService<IConfiguration>();
        if (configuration is not null && !configuration.GetValue(SyncOnStartupKey, true))
        {
            _logger.LogInformation("Permission catalogue sync at start-up is off ({Key}=false).", SyncOnStartupKey);
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            // role_permissions and user_permission_overrides are under RLS: the sync must see every tenant.
            scope.ServiceProvider.GetService<IWritableTenantContext>()?.SetAdminMode();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await SeedPermissionsAsync(db, cancellationToken);
        }
        catch (PermissionRetirementRefusedException ex)
        {
            _logger.LogCritical(ex, "Permission catalogue sync refused a retirement. Startup will stop.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Permission seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var clock = _services.GetService<IDateTimeProvider>() ?? new SystemDateTimeProvider();
        var synchronizer = new PermissionCatalogSynchronizer(db, clock, _logger);
        await synchronizer.SyncAsync(PermissionCatalog.Seeded, PermissionCatalog.Lifecycle, ct);
    }

    /// <summary>The rows this seeder writes, built from <see cref="PermissionCatalog.Seeded"/>.</summary>
    public static List<Permission> GetAllPermissions() =>
        PermissionCatalog.Seeded
            .Select(d => new Permission
            {
                Id = Guid.NewGuid(),
                Code = d.Code,
                Description = d.Description,
                Module = d.Module
            })
            .ToList();
}
