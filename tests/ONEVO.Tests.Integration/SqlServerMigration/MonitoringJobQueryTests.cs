using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.Monitoring.Notifications;
using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The location and wellness jobs only reach their employee and tray queries for an employee
/// with recent device snapshots. These run each job once on SQL Server with real repositories;
/// tray push and monitoring toggles are no-op fakes.
/// </summary>
public sealed class MonitoringJobQueryTests
{
    [Fact]
    public async Task LocationRuleJob_ReadsTheEmployeeAndLegalEntity_ForALocatedSnapshot()
    {
        var (h, db, employeeId) = await SeedAsync();
        db.DeviceStateSnapshots.Add(Snapshot(h, employeeId, DateTimeOffset.UtcNow.AddMinutes(-1), idle: false, located: true));
        await db.SaveChangesAsync();

        await new LocationRuleEvaluatorJob(Services(db), NullLogger<LocationRuleEvaluatorJob>.Instance)
            .RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WellnessJob_NotifiesALongIdleEmployee_AndLooksUpTheirTrayUser()
    {
        var (h, db, employeeId) = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        for (var minute = 40; minute >= 1; minute--)
            db.DeviceStateSnapshots.Add(Snapshot(h, employeeId, now.AddMinutes(-minute), idle: true, located: false));
        await db.SaveChangesAsync();

        await new WellnessRuleEvaluatorJob(Services(db), NullLogger<WellnessRuleEvaluatorJob>.Instance)
            .RunOnceAsync(CancellationToken.None);

        Assert.Contains(await db.MonitoringNotifications.AsNoTracking().ToListAsync(), n => n.EmployeeId == employeeId);
    }

    private static async Task<(MyTeamDb, ApplicationDbContext, Guid)> SeedAsync()
    {
        var h = await MyTeamDb.CreateAsync();
        await using (var setup = h.NewContext())
        {
            await setup.Database.ExecuteSqlRawAsync(TestDb.DisableForeignKeysSql("device_state_snapshots"));
            await setup.Database.ExecuteSqlRawAsync(TestDb.DisableForeignKeysSql("notifications"));
        }

        var db = h.NewContext();
        var employee = h.AddEmployee(db);
        await db.SaveChangesAsync();
        return (h, db, employee.Id);
    }

    private static DeviceStateSnapshot Snapshot(MyTeamDb h, Guid employeeId, DateTimeOffset at, bool idle, bool located) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = at, IdleSeconds = idle ? 60 : 0, IsIdle = idle,
        Latitude = located ? 6.9 : null, Longitude = located ? 79.8 : null, CreatedAt = at,
    };

    /// <summary>The job's scoped services over <paramref name="db"/>: each repository interface
    /// resolves to its Infrastructure implementation; services that leave the database are faked.</summary>
    private static IServiceProvider Services(ApplicationDbContext db)
    {
        var tenantContext = typeof(ApplicationDbContext).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.GetValue(db)).OfType<IWritableTenantContext>().First();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IDateTimeProvider>(new SystemDateTimeProvider());
        services.AddSingleton(tenantContext);
        services.AddSingleton<ITenantContext>(tenantContext);

        var infrastructure = typeof(ApplicationDbContext).Assembly.GetTypes();
        foreach (var name in new[]
                 {
                     "IDeviceStateSnapshotRepository", "INotificationRepository", "ITenantRepository",
                     "IDailyWorkLocationConfirmationRepository",
                 })
        {
            foreach (var contract in infrastructure.Concat(typeof(ITenantContext).Assembly.GetTypes())
                         .Where(t => t.IsInterface && t.Name == name))
            {
                var implementation = infrastructure.FirstOrDefault(i => i.IsClass && !i.IsAbstract && contract.IsAssignableFrom(i));
                if (implementation is not null)
                    services.AddSingleton(contract, sp => ActivatorUtilities.CreateInstance(sp, implementation));
            }
        }

        foreach (var name in new[] { "ITenantContextSwitcher", "IMonitoringToggleResolver", "ITrayPushNotifier" })
        {
            var contract = typeof(ITenantContext).Assembly.GetTypes().Concat(infrastructure)
                .First(t => t.IsInterface && t.Name == name);
            services.AddSingleton(contract, _ => QueryTranslationSweepTests.Fake.Create(contract));
        }

        return services.BuildServiceProvider();
    }
}
