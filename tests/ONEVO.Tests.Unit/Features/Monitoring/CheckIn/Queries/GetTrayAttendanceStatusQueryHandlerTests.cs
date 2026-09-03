using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayAttendanceStatus;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Queries;

public class GetTrayAttendanceStatusQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = DateOnly.FromDateTime(DateTime.UtcNow);

    private static (Mock<ITenantRepository> Tenants, Mock<ITenantContextSwitcher> Switcher) CreateTenantMocks()
    {
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Slug = "dapi" });
        var switcher = new Mock<ITenantContextSwitcher>();
        switcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return (tenants, switcher);
    }

    [Fact]
    public async Task Handle_OpenAttendanceRecord_ReturnsIsClockedInTrue()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);

        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var context = BuildContext();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.GetRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { ActualStart = startedAt, ActualEnd = null });

        var (tenants, switcher) = CreateTenantMocks();
        var sut = new GetTrayAttendanceStatusQueryHandler(device.Object, todayState.Object, attendance.Object, tenants.Object, switcher.Object);

        var result = await sut.Handle(new GetTrayAttendanceStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsClockedIn);
        Assert.Equal(startedAt, result.Value!.ClockedInAtUtc);
    }

    [Fact]
    public async Task Handle_NoAttendanceRecordForToday_ReturnsIsClockedInFalse()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);

        var context = BuildContext();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.GetRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);

        var (tenants, switcher) = CreateTenantMocks();
        var sut = new GetTrayAttendanceStatusQueryHandler(device.Object, todayState.Object, attendance.Object, tenants.Object, switcher.Object);

        var result = await sut.Handle(new GetTrayAttendanceStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsClockedIn);
        Assert.Null(result.Value!.ClockedInAtUtc);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsFailure()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(false);
        var todayState = new Mock<IAttendanceTodayStateService>();
        var attendance = new Mock<IAttendanceReadRepository>();

        var (tenants, switcher) = CreateTenantMocks();
        var sut = new GetTrayAttendanceStatusQueryHandler(device.Object, todayState.Object, attendance.Object, tenants.Object, switcher.Object);

        var result = await sut.Handle(new GetTrayAttendanceStatusQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    private static AttendanceTodayContext BuildContext() => new(
        new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, WorkModeId = 1 },
        new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo" },
        "Asia/Colombo",
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"),
        WorkDate,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
        AttendanceRecord.WorkAreaRemote,
        "active_employee_work_mode",
        new ClockInPolicy { Id = Guid.NewGuid(), RemoteTrayEnabled = true },
        "configured",
        new AllowedClockInMethods(false, true, false, false, false, null),
        new AttendanceLocalDayWindow(DateTimeOffset.UtcNow.AddHours(-8), DateTimeOffset.UtcNow.AddHours(16)));
}
