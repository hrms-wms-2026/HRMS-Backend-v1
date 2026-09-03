using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.TimeAttendance;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class LateClockInDailySummaryJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunTickAsync_NoActiveTenants_CompletesWithoutError()
    {
        var (provider, _) = BuildProvider(tenants: new List<Tenant>());
        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunTickAsync_SkipsLegalEntity_WhenShiftStartOffsetNotYetReached()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(11, 30)); // due at 13:30 UTC, now is 12:00
        var (provider, mocks) = BuildProvider(legalEntities: new List<LegalEntity> { legalEntity });
        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None);

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_SendsNotification_ToResolvedApprover_ForLateEmployee()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0)); // due at 11:00 UTC, now is 12:00
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var approverUserId = Guid.NewGuid();
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == lateEmployee.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), approverUserId, Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));
        mocks.Notifications
            .Setup(n => n.ExistsAsync(TenantId, approverUserId, "attendance_late_clockin_daily_summary",
                "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            TenantId, approverUserId, "attendance_late_clockin_daily_summary",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["lateCount"] == "1" && p["lateEmployees"].Contains("Jane Doe")),
            "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTickAsync_DoesNotResend_WhenNotificationAlreadyExists()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var approverUserId = Guid.NewGuid();
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), approverUserId, Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));
        mocks.Notifications
            .Setup(n => n.ExistsAsync(TenantId, approverUserId, "attendance_late_clockin_daily_summary",
                "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already sent

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_SkipsEmployee_WhenNoApproverResolvableAtAll()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.UnprocessableEntity("none"));

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None); // must not throw

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_IsolatesLegalEntityFailure_ClearsChangeTracker_AndStillProcessesNextLegalEntity()
    {
        var legalEntity1 = CreateLegalEntity(workStartTime: new TimeOnly(9, 0), id: LegalEntityId); // due at 11:00 UTC, now is 12:00
        var legalEntity2Id = Guid.NewGuid();
        var legalEntity2 = CreateLegalEntity(workStartTime: new TimeOnly(9, 0), id: legalEntity2Id);

        var failingEmployee = CreateEmployee(legalEntity1.Id, "Fail", "First");
        var okEmployee = CreateEmployee(legalEntity2.Id, "Second", "Ok");
        var failingRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = failingEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 20
        };
        var okRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = okEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 10
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity1, legalEntity2 },
            lateRecords: new List<AttendanceRecord> { failingRecord, okRecord },
            employeesById: new Dictionary<Guid, Employee>
            {
                [failingEmployee.Id] = failingEmployee,
                [okEmployee.Id] = okEmployee,
            });

        var okApproverUserId = Guid.NewGuid();

        // Legal entity 1's late employee blows up while resolving an approver - simulating a
        // transient failure inside RunForLegalEntityAsync, e.g. a DB blip.
        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == legalEntity1.Id),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated approver-resolution failure for legal entity 1"));

        // Legal entity 2's late employee resolves normally and must still be notified.
        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.LegalEntityId == legalEntity2.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), okApproverUserId, Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));

        // Simulate what a rolled-back ExecuteInTransactionAsync leaves behind: an Added-but-
        // never-saved entity still tracked by the tenant-scoped DbContext. Without the job's
        // ChangeTracker.Clear() call in its per-legal-entity catch block, this entity would
        // still be sitting in the tracker after legal entity 1 fails, and would remain there
        // (a real DbContext never clears the tracker on its own) through legal entity 2's
        // processing.
        var dbContext = provider.GetRequiredService<ApplicationDbContext>();
        dbContext.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = failingEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 20
        });
        Assert.NotEmpty(dbContext.ChangeTracker.Entries());

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None); // must not throw despite legal entity 1 failing

        // Legal entity 2 must still have been processed despite legal entity 1 throwing.
        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            TenantId, okApproverUserId, "attendance_late_clockin_daily_summary",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["lateCount"] == "1" && p["lateEmployees"].Contains("Second Ok")),
            "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Legal entity 1's failure must never have been notified for.
        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.Is<IReadOnlyDictionary<string, string>>(p => p["lateEmployees"].Contains("Fail First")),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The change tracker must have been cleared after legal entity 1's failure - proving
        // the job's ChangeTracker.Clear() call actually ran and actually removed the leaked
        // entity, not merely that RunTickAsync happened not to crash.
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static LegalEntity CreateLegalEntity(TimeOnly workStartTime, Guid? id = null) => new()
    {
        Id = id ?? LegalEntityId, TenantId = TenantId, Name = "Test Co", CountryCode = "US", CurrencyCode = "USD",
        IsActive = true, Timezone = "UTC", WorkStartTime = workStartTime, WorkEndTime = workStartTime.AddHours(8),
        StandardWorkingDays = "[1,2,3,4,5,6,7]"
    };

    private static Employee CreateEmployee(Guid legalEntityId, string firstName, string lastName) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), LegalEntityId = legalEntityId,
        EmployeeNumber = $"EMP-{Guid.NewGuid():N}"[..12], FirstName = firstName, LastName = lastName,
        Email = $"{firstName}.{lastName}@example.test", HireDate = new DateOnly(2026, 1, 1)
    };

    private sealed record Mocks(
        Mock<IAttendanceReadRepository> Attendance,
        Mock<IEmployeeAuthorityResolver> Authority,
        Mock<INotificationDispatcher> Dispatcher,
        Mock<INotificationRepository> Notifications);

    private static (IServiceProvider Provider, Mocks Mocks) BuildProvider(
        List<Tenant>? tenants = null,
        List<LegalEntity>? legalEntities = null,
        List<AttendanceRecord>? lateRecords = null,
        Dictionary<Guid, Employee>? employeesById = null)
    {
        tenants ??= new List<Tenant> { new() { Id = TenantId, Slug = "test-co", Status = TenantStatus.Active } };
        legalEntities ??= new List<LegalEntity>();
        lateRecords ??= new List<AttendanceRecord>();
        employeesById ??= new Dictionary<Guid, Employee>();

        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);

        var legalEntityRepo = new Mock<ILegalEntityRepository>();
        legalEntityRepo.Setup(r => r.ListActiveForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntities);

        var attendanceRepo = new Mock<IAttendanceReadRepository>();
        attendanceRepo.Setup(r => r.ListByStatusAsync(
                TenantId, It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lateRecords);

        var employeeRepo = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employeeRepo.Setup(r => r.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<Guid, Employee>)employeesById);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        var dispatcher = new Mock<INotificationDispatcher>();
        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(n => n.ExistsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        notifications.Setup(n => n.GetTemplateByCodeAsync(
                "attendance_late_clockin_daily_summary", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplate { Code = "attendance_late_clockin_daily_summary" });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<int>>, CancellationToken>((op, ct) => op(ct));

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(UtcNow);

        var switcher = new Mock<ITenantContextSwitcher>();
        switcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(tenantRepo.Object);
        services.AddSingleton(legalEntityRepo.Object);
        services.AddSingleton(attendanceRepo.Object);
        services.AddSingleton(employeeRepo.Object);
        services.AddSingleton(authority.Object);
        services.AddSingleton(dispatcher.Object);
        services.AddSingleton(notifications.Object);
        services.AddSingleton(unitOfWork.Object);
        services.AddSingleton(clock.Object);
        services.AddSingleton(switcher.Object);
        // Real (EF Core InMemory-backed) ApplicationDbContext so that
        // ProcessTenantAsync's per-legal-entity catch block can successfully
        // GetRequiredService<ApplicationDbContext>() and call ChangeTracker.Clear() -
        // matching production DI (services.AddDbContext<ApplicationDbContext>() in
        // DependencyInjection.cs), unlike the fully-mocked repositories used elsewhere
        // in this test class. Registered unconditionally so this gap can't resurface
        // silently in a future test that happens to exercise that catch block.
        services.AddSingleton(BuildInMemoryDbContext());

        return (services.BuildServiceProvider(), new Mocks(attendanceRepo, authority, dispatcher, notifications));
    }

    private static ApplicationDbContext BuildInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
