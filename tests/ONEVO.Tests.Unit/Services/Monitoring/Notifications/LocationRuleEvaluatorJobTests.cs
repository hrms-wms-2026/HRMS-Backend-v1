using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.Monitoring.Notifications;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Monitoring.Notifications;

public class LocationRuleEvaluatorJobTests
{
    private static ApplicationDbContext MakeDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        var currentUser = new Mock<ICurrentUser>();
        var publisher = new Mock<IPublisher>();
        var tenant = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenant.Object);
    }

    /// <summary>A tenant repo that resolves any id to an Active tenant - the job calls this once per
    /// distinct tenant id before touching that tenant's rows.</summary>
    private static ITenantRepository AnyTenantRepository()
    {
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Tenant
            {
                Id = id, Name = "Test", Slug = "test", Status = TenantStatus.Active
            });
        return tenants.Object;
    }

    private static IServiceProvider BuildServices(
        ApplicationDbContext db,
        IDeviceStateSnapshotRepository deviceState,
        IDailyWorkLocationConfirmationRepository confirmations,
        IEmployeeWorkLocationRepository workLocations,
        IClockInPolicyRepository clockInPolicies,
        IExpectedWorkAreaResolver expectedWorkAreas,
        INotificationRepository notifications,
        IDateTimeProvider clock,
        ITenantContextSwitcher? tenantSwitcher = null,
        ITenantRepository? tenants = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(deviceState);
        services.AddSingleton(confirmations);
        services.AddSingleton(workLocations);
        services.AddSingleton(clockInPolicies);
        services.AddSingleton(expectedWorkAreas);
        services.AddSingleton(notifications);
        services.AddSingleton(clock);
        services.AddSingleton<IWritableTenantContext>(new TenantContextAccessor());
        services.AddSingleton(tenantSwitcher ?? Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton(tenants ?? AnyTenantRepository());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunOnceAsync_SampleOutsideOfficeRadius_WritesAlert()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = MakeDb();
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = legalEntityId });
        db.LegalEntities.Add(new LegalEntity
        {
            Id = legalEntityId, TenantId = tenantId, OfficeLatitude = 6.9271, OfficeLongitude = 79.8612
        });
        await db.SaveChangesAsync();

        var deviceState = new Mock<IDeviceStateSnapshotRepository>();
        deviceState.Setup(d => d.GetActiveEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(tenantId, employeeId)]);
        deviceState.Setup(d => d.GetRecentAsync(tenantId, employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeviceStateSnapshot
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                CapturedAt = now, IsIdle = false, IdleSeconds = 0,
                Latitude = 6.0, Longitude = 79.0 // far from the office point above
            }]);

        var confirmations = new Mock<IDailyWorkLocationConfirmationRepository>();
        confirmations.Setup(c => c.GetForDateAsync(tenantId, employeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyWorkLocationConfirmation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                LocationType = DailyWorkLocationConfirmation.LocationTypeOffice, ConfirmedAt = now
            });

        var workLocations = new Mock<IEmployeeWorkLocationRepository>();

        var clockInPolicies = new Mock<IClockInPolicyRepository>();
        clockInPolicies.Setup(p => p.ListByLegalEntityAsync(tenantId, legalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClockInPolicy
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
                ScopeType = ClockInPolicy.ScopeFullCompany, IsActive = true,
                EffectiveFrom = DateOnly.MinValue, AllowedRadiusMeters = 300
            }]);

        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();

        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(n => n.ExistsRecentAsync(
                tenantId, employeeId, NotificationType.OutsideWorkLocationAlert, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var clock = new FakeDateTimeProvider { UtcNow = now };
        var services = BuildServices(db, deviceState.Object, confirmations.Object, workLocations.Object,
            clockInPolicies.Object, expectedWorkAreas.Object, notifications.Object, clock);

        var job = new LocationRuleEvaluatorJob(services, NullLogger<LocationRuleEvaluatorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        notifications.Verify(n => n.AddAsync(
            It.Is<Notification>(x => x.Type == NotificationType.OutsideWorkLocationAlert && x.EmployeeId == employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_SampleInsideRadius_DoesNotWriteAlert()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = MakeDb();
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = legalEntityId });
        db.LegalEntities.Add(new LegalEntity
        {
            Id = legalEntityId, TenantId = tenantId, OfficeLatitude = 6.9271, OfficeLongitude = 79.8612
        });
        await db.SaveChangesAsync();

        var deviceState = new Mock<IDeviceStateSnapshotRepository>();
        deviceState.Setup(d => d.GetActiveEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(tenantId, employeeId)]);
        deviceState.Setup(d => d.GetRecentAsync(tenantId, employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeviceStateSnapshot
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                CapturedAt = now, IsIdle = false, IdleSeconds = 0,
                Latitude = 6.9271, Longitude = 79.8612 // exactly at the office point
            }]);

        var confirmations = new Mock<IDailyWorkLocationConfirmationRepository>();
        confirmations.Setup(c => c.GetForDateAsync(tenantId, employeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyWorkLocationConfirmation
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                LocationType = DailyWorkLocationConfirmation.LocationTypeOffice, ConfirmedAt = now
            });

        var workLocations = new Mock<IEmployeeWorkLocationRepository>();

        var clockInPolicies = new Mock<IClockInPolicyRepository>();
        clockInPolicies.Setup(p => p.ListByLegalEntityAsync(tenantId, legalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClockInPolicy
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = legalEntityId,
                ScopeType = ClockInPolicy.ScopeFullCompany, IsActive = true,
                EffectiveFrom = DateOnly.MinValue, AllowedRadiusMeters = 300
            }]);

        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        var notifications = new Mock<INotificationRepository>();
        var clock = new FakeDateTimeProvider { UtcNow = now };
        var services = BuildServices(db, deviceState.Object, confirmations.Object, workLocations.Object,
            clockInPolicies.Object, expectedWorkAreas.Object, notifications.Object, clock);

        var job = new LocationRuleEvaluatorJob(services, NullLogger<LocationRuleEvaluatorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        notifications.Verify(n => n.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_NoConfirmationAndUnresolvedWorkArea_SkipsRatherThanGuessing()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = MakeDb();
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, LegalEntityId = legalEntityId });
        db.LegalEntities.Add(new LegalEntity { Id = legalEntityId, TenantId = tenantId });
        await db.SaveChangesAsync();

        var deviceState = new Mock<IDeviceStateSnapshotRepository>();
        deviceState.Setup(d => d.GetActiveEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(tenantId, employeeId)]);
        deviceState.Setup(d => d.GetRecentAsync(tenantId, employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeviceStateSnapshot
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                CapturedAt = now, IsIdle = false, IdleSeconds = 0, Latitude = 6.0, Longitude = 79.0
            }]);

        var confirmations = new Mock<IDailyWorkLocationConfirmationRepository>();
        confirmations.Setup(c => c.GetForDateAsync(tenantId, employeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyWorkLocationConfirmation?)null);

        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        expectedWorkAreas.Setup(r => r.ResolveAsync(
                It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<ExpectedWorkAreaResolution>.Conflict("not configured"));

        var notifications = new Mock<INotificationRepository>();
        var clock = new FakeDateTimeProvider { UtcNow = now };
        var services = BuildServices(db, deviceState.Object, confirmations.Object, new Mock<IEmployeeWorkLocationRepository>().Object,
            new Mock<IClockInPolicyRepository>().Object, expectedWorkAreas.Object, notifications.Object, clock);

        var job = new LocationRuleEvaluatorJob(services, NullLogger<LocationRuleEvaluatorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        notifications.Verify(n => n.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_EntersAdminModeThenSwitchesContextOncePerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var empA1 = Guid.NewGuid();
        var empA2 = Guid.NewGuid();
        var empB1 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = MakeDb();
        await db.SaveChangesAsync();

        var contextModesAtSweep = new List<TenantContextMode>();
        var writableContext = new TenantContextAccessor();

        var deviceState = new Mock<IDeviceStateSnapshotRepository>();
        deviceState.Setup(d => d.GetActiveEmployeeKeysAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                contextModesAtSweep.Add(writableContext.ContextMode);
                return new List<(Guid TenantId, Guid EmployeeId)>
                {
                    (tenantA, empA1), (tenantA, empA2), (tenantB, empB1)
                };
            });
        // No located sample -> each employee is skipped after the context switch; keeps the test
        // focused on the tenant-context establishment, not the alerting path.
        deviceState.Setup(d => d.GetRecentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tenantSwitcher = new Mock<ITenantContextSwitcher>();
        var switchedTenantIds = new List<Guid>();
        tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Callback((TenantRegistryEntry e, CancellationToken _) => switchedTenantIds.Add(e.TenantId))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(deviceState.Object);
        services.AddSingleton(new Mock<IDailyWorkLocationConfirmationRepository>().Object);
        services.AddSingleton(new Mock<IEmployeeWorkLocationRepository>().Object);
        services.AddSingleton(new Mock<IClockInPolicyRepository>().Object);
        services.AddSingleton(new Mock<IExpectedWorkAreaResolver>().Object);
        services.AddSingleton(new Mock<INotificationRepository>().Object);
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider { UtcNow = now });
        services.AddSingleton<IWritableTenantContext>(writableContext);
        services.AddSingleton(tenantSwitcher.Object);
        services.AddSingleton(AnyTenantRepository());

        var job = new LocationRuleEvaluatorJob(services.BuildServiceProvider(), NullLogger<LocationRuleEvaluatorJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        contextModesAtSweep.Should().ContainSingle().Which.Should().Be(TenantContextMode.Admin);
        switchedTenantIds.Should().BeEquivalentTo([tenantA, tenantB]);
    }
}
