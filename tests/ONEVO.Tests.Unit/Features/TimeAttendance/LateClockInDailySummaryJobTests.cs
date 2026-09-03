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
    public async Task RunTickAsync_LateNightShift_BecomesDue_AfterMidnightRollover()
    {
        // CRITICAL regression test. Shift starts 22:00 local (Sept 2); the 2h offset pushes the
        // due instant to exactly Sept 3 00:00. UtcNow below is past that instant AND past
        // midnight into the next calendar day - this is exactly the case where
        // AttendanceScheduleResolver.Resolve's WorkDate has already rolled over to Sept 3, so a
        // naive "today's shift start + offset" comparison can never fire (see the long comment in
        // ProcessTenantAsync). The fix must resolve the shift's actual start day (Sept 2), not
        // "today", both for the due check and for which day's late records get queried.
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(22, 0), workEndTime: new TimeOnly(23, 59));
        var utcNow = new DateTimeOffset(2026, 9, 3, 0, 30, 0, TimeSpan.Zero);
        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity }, utcNow: utcNow);

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            TenantId, new DateOnly(2026, 9, 2), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTickAsync_ShiftStartingAt2330_BecomesDue_AfterMidnightRollover()
    {
        // Regression guard past the exactly-midnight 22:00 case: 23:30 start + 2h offset is due
        // at Sept 3 01:30, well past (not exactly at) the WorkDate rollover boundary - proves the
        // fix generalizes rather than happening to work only for the degenerate 24:00:00 case.
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(23, 30), workEndTime: new TimeOnly(23, 59));
        var utcNow = new DateTimeOffset(2026, 9, 3, 1, 45, 0, TimeSpan.Zero);
        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity }, utcNow: utcNow);

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            TenantId, new DateOnly(2026, 9, 2), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()),
            Times.Once);
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
    public async Task RunTickAsync_IsolatesLegalEntityFailure_ClearsTracking_AndStillProcessesNextLegalEntity()
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

        // The job must have gone through IUnitOfWork.ClearTracking() exactly once - proving the
        // per-legal-entity catch block actually ran its cleanup step after legal entity 1's
        // failure, via the same IUnitOfWork abstraction every other DB access in this job uses
        // (not a direct, unprecedented ApplicationDbContext.ChangeTracker.Clear() reach-around).
        mocks.UnitOfWork.Verify(u => u.ClearTracking(), Times.Once);
    }

    [Fact]
    public async Task RunTickAsync_ContinuesToNextTenant_WhenTenantSwitchThrowsForFirstTenant()
    {
        // Exercises the per-TENANT catch in RunTickAsync (as opposed to the per-legal-entity one
        // covered above): tenant A's SwitchToTenantAsync throws outright, before any legal entity
        // work even starts for it, and tenant B must still be processed in the same tick.
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var tenantA = new Tenant { Id = tenantAId, Slug = "tenant-a", Status = TenantStatus.Active };
        var tenantB = new Tenant { Id = tenantBId, Slug = "tenant-b", Status = TenantStatus.Active };
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));

        var (provider, mocks) = BuildProvider(
            tenants: new List<Tenant> { tenantA, tenantB },
            legalEntities: new List<LegalEntity> { legalEntity });

        mocks.Switcher
            .Setup(s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == tenantAId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated tenant switch failure for tenant A"));

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None); // must not throw despite tenant A failing

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            tenantBId, It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()),
            Times.Once);
        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            tenantAId, It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_LogsAndSkips_WhenNotificationTemplateNotSeeded()
    {
        // Covers the fail-closed throw added for the "template not seeded" case: it must be
        // caught and logged at the legal-entity level (RunTickAsync itself must not throw), and
        // must never reach SendTemplatedAsync. Discriminator that proves the throw actually fired
        // rather than one of the three earlier early-returns in RunForLegalEntityAsync silently
        // matching first: run two ticks on the same job instance and require ListByStatusAsync to
        // be called both times. An early return (no late records / no matching employee / no
        // resolved approver) would have recorded success and skipped the second tick's re-query;
        // the template-null throw is caught AFTER the success-only "recorded" write, so the legal
        // entity remains due and gets re-queried on the next tick.
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
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));
        // Overrides BuildProvider's default non-null template setup - last setup wins in Moq.
        mocks.Notifications
            .Setup(n => n.GetTemplateByCodeAsync("attendance_late_clockin_daily_summary", It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationTemplate?)null);

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None); // must not throw out of RunTickAsync
        await job.RunTickAsync(CancellationToken.None); // still due - proves the failure wasn't recorded as a success

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_SkipsLegalEntity_WithZeroLateRecords_AndDoesNotRequeryForRestOfDay()
    {
        // No late records at all: RunForLegalEntityAsync's first early return. Must not send
        // anything and must not throw. The second half of this test proves the legal entity still
        // gets recorded as "done for today" despite the early return, so it isn't re-queried every
        // 15 minutes for the rest of the day - both ticks below run against the SAME job instance
        // (the dictionary tracking "already ran" is an instance field), and ListByStatusAsync must
        // only have been called once total even though the legal entity remains "due" both times.
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));
        var (provider, mocks) = BuildProvider(legalEntities: new List<LegalEntity> { legalEntity });

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            TenantId, It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()),
            Times.Once);
        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static LegalEntity CreateLegalEntity(TimeOnly workStartTime, Guid? id = null, TimeOnly? workEndTime = null) => new()
    {
        Id = id ?? LegalEntityId, TenantId = TenantId, Name = "Test Co", CountryCode = "US", CurrencyCode = "USD",
        IsActive = true, Timezone = "UTC", WorkStartTime = workStartTime,
        WorkEndTime = workEndTime ?? workStartTime.AddHours(8),
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
        Mock<INotificationRepository> Notifications,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<ITenantContextSwitcher> Switcher);

    private static (IServiceProvider Provider, Mocks Mocks) BuildProvider(
        List<Tenant>? tenants = null,
        List<LegalEntity>? legalEntities = null,
        List<AttendanceRecord>? lateRecords = null,
        Dictionary<Guid, Employee>? employeesById = null,
        DateTimeOffset? utcNow = null)
    {
        tenants ??= new List<Tenant> { new() { Id = TenantId, Slug = "test-co", Status = TenantStatus.Active } };
        legalEntities ??= new List<LegalEntity>();
        lateRecords ??= new List<AttendanceRecord>();
        employeesById ??= new Dictionary<Guid, Employee>();

        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);

        // Not keyed to the single default TenantId constant: tests covering multi-tenant ticks
        // (e.g. the per-tenant-catch test) need these to resolve for whichever tenant id RunTickAsync
        // is currently processing.
        var legalEntityRepo = new Mock<ILegalEntityRepository>();
        legalEntityRepo.Setup(r => r.ListActiveForTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntities);

        var attendanceRepo = new Mock<IAttendanceReadRepository>();
        attendanceRepo.Setup(r => r.ListByStatusAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lateRecords);

        var employeeRepo = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employeeRepo.Setup(r => r.ListByIdsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
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

        // A plain Mock<IUnitOfWork> (rather than a real ApplicationDbContext) is enough for every
        // test in this class: ExecuteInTransactionAsync just runs the operation passed to it, and
        // ClearTracking() is a void member Moq auto-implements as a no-op that Verify can still
        // assert against - no real DbContext/EF InMemory provider needed anywhere in this file.
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<int>>, CancellationToken>((op, ct) => op(ct));

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(utcNow ?? UtcNow);

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

        return (services.BuildServiceProvider(),
            new Mocks(attendanceRepo, authority, dispatcher, notifications, unitOfWork, switcher));
    }
}
