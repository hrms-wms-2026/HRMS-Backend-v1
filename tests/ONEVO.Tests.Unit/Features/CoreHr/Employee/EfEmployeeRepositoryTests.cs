using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EfEmployeeRepositoryTests
{
    [Fact]
    public async Task ListVisibleAsync_ReturnsAllTenantEmployees_WhenScopeIsUnrestricted()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.AddRange(NewEmployee(tenantId, "E-001"), NewEmployee(tenantId, "E-002"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ListVisibleAsync_ExcludesEmployeesOutsideCoverage_WhenScopeIsRestrictedByDepartment()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var coveredDeptId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();

        var inCoverage = NewEmployee(tenantId, "E-001");
        inCoverage.DepartmentId = coveredDeptId;
        var outOfCoverage = NewEmployee(tenantId, "E-002");
        outOfCoverage.DepartmentId = otherDeptId;

        db.Employees.AddRange(inCoverage, outOfCoverage);
        db.Departments.AddRange(
            new Department { Id = coveredDeptId, TenantId = tenantId, Name = "Covered", LegalEntityId = Guid.NewGuid() },
            new Department { Id = otherDeptId, TenantId = tenantId, Name = "Other", LegalEntityId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = new EmployeeVisibilityScope(
            false, null, new HashSet<Guid>(), new HashSet<Guid> { coveredDeptId }, new HashSet<Guid>());

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(inCoverage.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_AlwaysIncludesCallersOwnEmployeeRow_EvenOutsideCoverage()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var self = NewEmployee(tenantId, "E-001");
        db.Employees.Add(self);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = new EmployeeVisibilityScope(
            false, self.Id, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), 1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(self.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_RestrictsToGivenIds_IgnoringScope_WhenRestrictToEmployeeIdsIsSet()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var allowed = NewEmployee(tenantId, "E-001");
        var notAllowed = NewEmployee(tenantId, "E-002");
        db.Employees.AddRange(allowed, notAllowed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, null, new HashSet<Guid> { allowed.Id }),
            1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(allowed.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_ReturnsEmpty_WhenRestrictToEmployeeIdsIsEmptySet()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId, "E-001"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, null, new HashSet<Guid>()),
            1, 25, CancellationToken.None);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListVisibleAsync_AppliesSearchFilter_WithinRestrictToEmployeeIds()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        // Explicit non-colliding emails: NewEmployee's default random email is a hex GUID
        // string, which has a small but real chance of containing "ada" as a substring
        // (a/d are valid hex digits) and flakily matching Bob too.
        var ada = NewEmployee(tenantId, "E-001", email: "ada@test.dev");
        ada.FirstName = "Ada";
        var bob = NewEmployee(tenantId, "E-002", email: "bob@test.dev");
        bob.FirstName = "Bob";
        db.Employees.AddRange(ada, bob);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var (items, total) = await repo.ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter("ada", null, null, new HashSet<Guid> { ada.Id, bob.Id }),
            1, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal(ada.Id, items[0].Id);
    }

    [Fact]
    public async Task ListVisibleAsync_ResolvesReportingManagerFromHierarchyClosure()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var manager = NewEmployee(tenantId, "MGR-001");
        var report = NewEmployee(tenantId, "E-001");
        db.Employees.AddRange(manager, report);
        db.EmployeeHierarchyClosures.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
        {
            TenantId = tenantId,
            AncestorEmployeeId = manager.Id,
            DescendantEmployeeId = report.Id,
            Depth = 1,
            SourcePositionAssignmentId = Guid.NewGuid(),
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), report.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(manager.Id, result!.ReportingManagerId);
        Assert.Equal($"{manager.FirstName} {manager.LastName}", result.ReportingManagerName);
    }

    [Fact]
    public async Task GetVisibleByIdAsync_ResolvesWorkModeLabelFromLookupTable()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.WorkModeId = 2;
        db.Employees.Add(employee);
        db.WorkModes.Add(new ONEVO.Domain.Lookups.WorkMode { Id = 2, Code = "remote", Label = "Remote", IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Remote", result!.WorkModeLabel);
    }

    [Fact]
    public async Task GetVisibleByIdAsync_FallsBackToWorkModeIdString_WhenNoLookupRowMatches()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.WorkModeId = 99;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("99", result!.WorkModeLabel);
    }

    [Fact]
    public async Task ListVisibleAsync_LeavesReportingManagerNull_WhenNoClosureRowExists()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var result = await repo.GetVisibleByIdAsync(tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.ReportingManagerId);
    }

    [Fact]
    public async Task EmailExistsAsync_IsCaseInsensitive_AndTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.Email = "Ada@Test.Dev";
        db.Employees.Add(employee);
        db.Employees.Add(NewEmployee(otherTenantId, "E-001-B", email: "shared@test.dev"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);

        Assert.True(await repo.EmailExistsAsync(tenantId, "ada@test.dev", null, CancellationToken.None));
        Assert.False(await repo.EmailExistsAsync(otherTenantId, "ada@test.dev", null, CancellationToken.None));
    }

    [Fact]
    public async Task EmailExistsAsync_ExcludesGivenId()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);
        var exists = await repo.EmailExistsAsync(tenantId, employee.Email, employee.Id, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task EmployeeNumberExistsAsync_IsTenantScoped()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Employees.Add(NewEmployee(tenantId, "SHARED-001"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfEmployeeRepository(db);

        Assert.True(await repo.EmployeeNumberExistsAsync(tenantId, "SHARED-001", null, CancellationToken.None));
        Assert.False(await repo.EmployeeNumberExistsAsync(otherTenantId, "SHARED-001", null, CancellationToken.None));
    }

    [Fact]
    public async Task ListVisibleAsync_MarksActiveEmployeeWithoutTodayRecordAfterLocalStart()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, total) = await new EfEmployeeRepository(db).ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1,
            25,
            CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero)));

        Assert.Equal(1, total);
        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.True(summary!.ShowNotClockedInWarning);
        Assert.True(summary.ShouldHaveClockedIn);
        Assert.False(summary.HasClockedInToday);
        Assert.Equal(new DateOnly(2026, 8, 21), summary.WorkDate);
        Assert.Equal("Asia/Colombo", summary.Timezone);
        Assert.Equal("09:00", summary.ScheduledStartTime);
        Assert.Equal("Still has not clocked in", summary.WarningLabel);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotMarkEmployeeWithActualStartForToday()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21),
            ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1,
            25,
            CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero)));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.False(summary!.ShowNotClockedInWarning);
        Assert.False(summary.ShouldHaveClockedIn);
        Assert.True(summary.HasClockedInToday);
        Assert.Null(summary.WarningLabel);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotMarkEmployeeBeforeLocalScheduledStart()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1,
            25,
            CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 3, 0, 0, TimeSpan.Zero)));

        Assert.False(Assert.Single(items).AttendanceSummary!.ShowNotClockedInWarning);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotMarkEmployeeOnConfiguredNonWorkingDay()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1,
            25,
            CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 23, 4, 0, 0, TimeSpan.Zero)));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.False(summary!.ShowNotClockedInWarning);
        Assert.Equal(new DateOnly(2026, 8, 23), summary.WorkDate);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotMarkEmployeeWhenScheduleIsNotConfigured()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(new LegalEntity { Id = legalEntityId, TenantId = tenantId, Timezone = null, WorkStartTime = null, WorkEndTime = null });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            tenantId,
            EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1,
            25,
            CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero)));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.False(summary!.ShowNotClockedInWarning);
        Assert.False(summary.ShouldHaveClockedIn);
        Assert.Null(summary.ScheduledStartTime);
        Assert.Null(summary.WarningLabel);
    }

    [Fact]
    public async Task ListVisibleAsync_SortsWarningsBeforeStableEmployeeOrderBeforePagination()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var warning = NewEmployee(tenantId, "E-002");
        warning.FirstName = "Zoe";
        warning.LastName = "Zulu";
        warning.LegalEntityId = legalEntityId;
        var normal = NewEmployee(tenantId, "E-001");
        normal.FirstName = "Ada";
        normal.LastName = "Aardvark";
        normal.LegalEntityId = legalEntityId;
        db.Employees.AddRange(warning, normal);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = normal.Id,
            Date = new DateOnly(2026, 8, 21),
            ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var filter = new EmployeeListFilter(null, null, legalEntityId, new[] { warning.Id, normal.Id });
        var repo = new EfEmployeeRepository(db);
        var page1 = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), filter, 1, 1, CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero)));
        var page2 = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), filter, 2, 1, CancellationToken.None,
            new EmployeeListAttendanceOptions(new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero)));

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(warning.Id, Assert.Single(page1.Items).Id);
        Assert.True(page1.Items[0].AttendanceSummary!.ShowNotClockedInWarning);
        Assert.Equal(normal.Id, Assert.Single(page2.Items).Id);
        Assert.False(page2.Items[0].AttendanceSummary!.ShowNotClockedInWarning);
    }

    [Fact]
    public async Task ListVisibleAsync_ShowsIdleWarning_WhenRecentLongIdleAlertExists_AndNoHigherPriorityWarning()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.MonitoringNotifications.Add(new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.UserId,
            Type = NotificationType.LongIdleAlert, Title = "Still there?", Message = "idle",
            CreatedAt = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db), checkIns: new EfCheckInRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("idle_too_long", summary!.AttentionType);
        Assert.Equal("warning", summary.AttentionSeverity);
        toggles.Verify(
            t => t.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListVisibleAsync_ShowsOutsideWorkLocationWarning_WhenRecentOutsideWorkLocationAlertExists()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.MonitoringNotifications.Add(new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.UserId,
            Type = NotificationType.OutsideWorkLocationAlert, Title = "Left the work area", Message = "outside",
            CreatedAt = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db), checkIns: new EfCheckInRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("outside_work_location", summary!.AttentionType);
        Assert.Equal("warning", summary.AttentionSeverity);
    }

    [Fact]
    public async Task ListVisibleAsync_ShowsCameraSkippedWarning_WhenClockedInWithoutFaceScan_AndCameraRequired()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.EmployeeCheckIns.Add(new EmployeeCheckIn
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = employee.UserId,
            DeviceRegistrationId = Guid.NewGuid(), FaceScanId = null,
            CheckedInAt = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db), checkIns: new EfCheckInRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("camera_verification_skipped", summary!.AttentionType);
        Assert.Equal("warning", summary.AttentionSeverity);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotShowCameraWarning_WhenIdentityVerificationNotRequired()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db), checkIns: new EfCheckInRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Null(summary!.AttentionType);
    }

    [Fact]
    public async Task ListVisibleAsync_KeepsHigherPriorityWarning_OverIdleOrCameraSignals()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        // No attendance record -> not_clocked_in (higher priority than idle/camera).
        db.MonitoringNotifications.Add(new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.UserId,
            Type = NotificationType.LongIdleAlert, Title = "Still there?", Message = "idle",
            CreatedAt = now.AddMinutes(-10),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db), checkIns: new EfCheckInRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("not_clocked_in", summary!.AttentionType);
    }

    [Fact]
    public async Task ListVisibleAsync_ShowsOutsideWorkLocationWarning_WhenCheckInFarFromOfficeAndOnsite()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntityWithOffice(tenantId, legalEntityId, officeLat: 6.9271, officeLon: 79.8612));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 200));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.EmployeeCheckIns.Add(new EmployeeCheckIn
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = employee.UserId,
            DeviceRegistrationId = Guid.NewGuid(), Latitude = 6.8000, Longitude = 79.9000,
            CheckedInAt = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var expectedWorkAreas = OnsiteExpectedWorkAreaResolver();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("outside_work_location", summary!.AttentionType);
        Assert.Equal("warning", summary.AttentionSeverity);
    }

    [Fact]
    public async Task ListVisibleAsync_ShowsOutsideWorkLocationWarning_WhenOnsiteCheckInHasNoLocation()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntityWithOffice(tenantId, legalEntityId, officeLat: 6.9271, officeLon: 79.8612));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 200));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        // No EmployeeCheckIn row at all - location should have been captured but was not.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var expectedWorkAreas = OnsiteExpectedWorkAreaResolver();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Equal("outside_work_location", summary!.AttentionType);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotShowLocationWarning_WhenCheckInWithinRadius()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntityWithOffice(tenantId, legalEntityId, officeLat: 6.9271, officeLon: 79.8612));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 500));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.EmployeeCheckIns.Add(new EmployeeCheckIn
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = employee.UserId,
            DeviceRegistrationId = Guid.NewGuid(), Latitude = 6.9271, Longitude = 79.8612,
            CheckedInAt = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var expectedWorkAreas = OnsiteExpectedWorkAreaResolver();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Null(summary!.AttentionType);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotShowLocationWarning_WhenEmployeeExpectedRemote()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntityWithOffice(tenantId, legalEntityId, officeLat: 6.9271, officeLon: 79.8612));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 200));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        db.EmployeeCheckIns.Add(new EmployeeCheckIn
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = employee.UserId,
            DeviceRegistrationId = Guid.NewGuid(), Latitude = 6.8000, Longitude = 79.9000,
            CheckedInAt = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        expectedWorkAreas.Setup(r => r.ResolveAsync(
                It.IsAny<EmployeeEntity>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(new ExpectedWorkAreaResolution("remote", "Asia/Colombo", "active_employee_work_mode")));
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Null(summary!.AttentionType);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotShowLocationWarning_WhenOfficeLocationNotConfigured()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        // No office lat/lng configured on this legal entity.
        db.LegalEntities.Add(WorkingLegalEntity(tenantId, legalEntityId));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 200));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var expectedWorkAreas = OnsiteExpectedWorkAreaResolver();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Null(summary!.AttentionType);
        expectedWorkAreas.Verify(
            r => r.ResolveAsync(It.IsAny<EmployeeEntity>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListVisibleAsync_DoesNotShowLocationWarning_WhenLocationTrackingToggleDisabled()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E-001");
        employee.LegalEntityId = legalEntityId;
        var now = new DateTimeOffset(2026, 8, 21, 5, 0, 0, TimeSpan.Zero);
        db.Employees.Add(employee);
        db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.LegalEntities.Add(WorkingLegalEntityWithOffice(tenantId, legalEntityId, officeLat: 6.9271, officeLon: 79.8612));
        db.ClockInPolicies.Add(NewFullCompanyClockInPolicy(tenantId, legalEntityId, allowedRadiusMeters: 200));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            Date = new DateOnly(2026, 8, 21), ActualStart = new DateTimeOffset(2026, 8, 21, 4, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var toggles = new Mock<IMonitoringToggleResolver>();
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        toggles.Setup(t => t.IsEnabledAsync(
                tenantId, employee.UserId, MonitoringCapability.IdentityVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var expectedWorkAreas = OnsiteExpectedWorkAreaResolver();
        var repo = new EfEmployeeRepository(
            db, toggles: toggles.Object, notifications: new EfNotificationRepository(db),
            checkIns: new EfCheckInRepository(db), expectedWorkAreas: expectedWorkAreas.Object,
            clockInPolicies: new EfClockInPolicyRepository(db));

        var (items, _) = await repo.ListVisibleAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(),
            new EmployeeListFilter(null, null, legalEntityId, new[] { employee.Id }),
            1, 25, CancellationToken.None, new EmployeeListAttendanceOptions(now));

        var summary = Assert.Single(items).AttendanceSummary;
        Assert.NotNull(summary);
        Assert.Null(summary!.AttentionType);
    }

    private static Mock<IExpectedWorkAreaResolver> OnsiteExpectedWorkAreaResolver()
    {
        var mock = new Mock<IExpectedWorkAreaResolver>();
        mock.Setup(r => r.ResolveAsync(
                It.IsAny<EmployeeEntity>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(new ExpectedWorkAreaResolution("onsite", "Asia/Colombo", "active_employee_work_mode")));
        return mock;
    }

    private static LegalEntity WorkingLegalEntityWithOffice(
        Guid tenantId, Guid legalEntityId, double officeLat, double officeLon)
    {
        var entity = WorkingLegalEntity(tenantId, legalEntityId);
        entity.OfficeLatitude = officeLat;
        entity.OfficeLongitude = officeLon;
        return entity;
    }

    /// <summary>The radius for the on-site/remote location checks now comes from ClockInPolicy
    /// (shared with the "Allowed distance" field on the Clock-in Policy screen), not LegalEntity.</summary>
    private static ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy NewFullCompanyClockInPolicy(
        Guid tenantId, Guid legalEntityId, int allowedRadiusMeters) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LegalEntityId = legalEntityId,
        Name = "Default",
        ScopeType = ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy.ScopeFullCompany,
        EffectiveFrom = new DateOnly(2020, 1, 1),
        AllowedRadiusMeters = allowedRadiusMeters,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static LegalEntity WorkingLegalEntity(Guid tenantId, Guid legalEntityId) => new()
    {
        Id = legalEntityId,
        TenantId = tenantId,
        Timezone = "Asia/Colombo",
        StandardWorkingDays = "[1,2,3,4,5]",
        WorkStartTime = new TimeOnly(9, 0),
        WorkEndTime = new TimeOnly(17, 30),
    };

    private static EmployeeEntity NewEmployee(Guid tenantId, string employeeNumber, string? email = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = Guid.NewGuid(),
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = employeeNumber,
        Email = email ?? $"{Guid.NewGuid():N}@test.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
