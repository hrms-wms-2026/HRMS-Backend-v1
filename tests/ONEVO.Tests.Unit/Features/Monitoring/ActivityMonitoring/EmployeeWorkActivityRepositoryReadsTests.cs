using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class EmployeeWorkActivityRepositoryReadsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

    private static ActivitySnapshot Snap(DateTimeOffset at, int active, int idle = 0, Guid? employeeId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId ?? EmployeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = at, ActiveSeconds = active, IdleSeconds = idle, ForegroundProcessName = "code.exe", CreatedAt = at
    };

    private static AppUsageSnapshot App(DateTimeOffset at, string process) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
        CapturedAt = at, ProcessName = process, CreatedAt = at
    };

    [Fact]
    public async Task ActiveSecondsByHalfHour_SumsPerUtcHalfHourSlot_InsideTheWindowOnly()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(
            Snap(From.AddMinutes(10), 60), Snap(From.AddMinutes(20), 30),   // slot 00:00
            Snap(From.AddMinutes(40), 45),                                   // slot 00:30
            Snap(To.AddMinutes(5), 60),                                      // outside
            Snap(From.AddMinutes(10), 60, employeeId: Guid.NewGuid()));      // other employee
        await db.SaveChangesAsync();

        var rows = await new EfActivitySnapshotRepository(db).GetActiveSecondsByHalfHourAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.SlotStartUtc == From && r.ActiveSeconds == 90);
        Assert.Contains(rows, r => r.SlotStartUtc == From.AddMinutes(30) && r.ActiveSeconds == 45);
    }

    [Fact]
    public async Task LastActiveAt_IsTheLatestSnapshotWithActiveSeconds()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(Snap(From.AddHours(3), 60), Snap(From.AddHours(4), 0, idle: 60));
        await db.SaveChangesAsync();

        var last = await new EfActivitySnapshotRepository(db).GetLastActiveAtAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(From.AddHours(3), last);
    }

    [Fact]
    public async Task WindowsByEmployeeRange_ReturnsOrderedWindowsInRange()
    {
        await using var db = BuildDb();
        db.ActivitySnapshots.AddRange(Snap(From.AddHours(2), 60), Snap(From.AddHours(1), 60), Snap(To, 60));
        await db.SaveChangesAsync();

        var rows = await new EfActivitySnapshotRepository(db).GetWindowsByEmployeeRangeAsync(TenantId, EmployeeId, From, To, default);

        Assert.Equal(new[] { From.AddHours(1), From.AddHours(2) }, rows.Select(r => r.CapturedAt));
    }

    [Fact]
    public async Task AppMinutesByProcess_CountsSamplesAndLastSeen_PerProcess()
    {
        await using var db = BuildDb();
        db.AppUsageSnapshots.AddRange(
            App(From.AddMinutes(1), "code.exe"), App(From.AddMinutes(2), "code.exe"),
            App(From.AddMinutes(3), "chrome.exe"), App(To.AddMinutes(1), "code.exe"));
        await db.SaveChangesAsync();

        var rows = await new EfAppUsageSnapshotRepository(db).GetMinutesByProcessAsync(TenantId, EmployeeId, From, To, default);

        var code = Assert.Single(rows, r => r.ProcessName == "code.exe");
        Assert.Equal(2, code.Samples);
        Assert.Equal(From.AddMinutes(2), code.LastCapturedAt);
    }

    [Fact]
    public async Task AppSamplesForProcesses_ReturnsOnlyRequestedProcesses_Ordered()
    {
        await using var db = BuildDb();
        db.AppUsageSnapshots.AddRange(
            App(From.AddMinutes(2), "code.exe"), App(From.AddMinutes(1), "code.exe"), App(From.AddMinutes(3), "chrome.exe"));
        await db.SaveChangesAsync();

        var rows = await new EfAppUsageSnapshotRepository(db)
            .GetSamplesForProcessesAsync(TenantId, EmployeeId, From, To, new[] { "code.exe" }, default);

        Assert.Equal(new[] { From.AddMinutes(1), From.AddMinutes(2) }, rows.Select(r => r.CapturedAt));
    }

    [Fact]
    public async Task MeetingSignalsByRange_ReturnsSignalsInRange()
    {
        await using var db = BuildDb();
        db.MeetingSignals.AddRange(
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = From.AddHours(1), IsMeetingAppRunning = true },
            new MeetingSignal { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(), CapturedAt = To, IsMeetingAppRunning = true });
        await db.SaveChangesAsync();

        var rows = await new EfMeetingSignalRepository(db).GetByEmployeeRangeAsync(TenantId, EmployeeId, From, To, default);

        Assert.Single(rows);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
