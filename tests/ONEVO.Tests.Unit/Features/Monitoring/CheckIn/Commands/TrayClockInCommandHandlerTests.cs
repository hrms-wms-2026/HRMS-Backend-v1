using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.TrayClockIn;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class TrayClockInCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 17, 30, 0, TimeSpan.Zero);

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

    private static TrayClockInCommandHandler CreateSut(
        Mock<ITrayCurrentDevice> device,
        Mock<IAttendanceTodayStateService> todayState,
        ClockInCommandHandler inner)
    {
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);
        var (tenants, switcher) = CreateTenantMocks();
        return new TrayClockInCommandHandler(device.Object, todayState.Object, inner, tenants.Object, switcher.Object);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsFailure()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(false);
        var todayState = new Mock<IAttendanceTodayStateService>();
        var (tenants, switcher) = CreateTenantMocks();
        var sut = new TrayClockInCommandHandler(device.Object, todayState.Object, inner: null!, tenants.Object, switcher.Object);

        var result = await sut.Handle(new TrayClockInCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ContextResolutionFails_PropagatesFailure()
    {
        var device = new Mock<ITrayCurrentDevice>();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("no employee"));
        var sut = CreateSut(device, todayState, inner: null!);

        var result = await sut.Handle(new TrayClockInCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ContextResolves_DelegatesToInnerHandlerWithTraySource()
    {
        var device = new Mock<ITrayCurrentDevice>();
        var context = BuildContext();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(context));

        var innerTodayState = new Mock<IAttendanceTodayStateService>();
        innerTodayState.Setup(t => t.GetTodayAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(CreateTodayResponse()));
        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.GetTrackedRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);
        attendance.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<bool>>> operation, CancellationToken ct) => operation(ct));
        var inner = new ClockInCommandHandler(innerTodayState.Object, attendance.Object, unitOfWork.Object);

        var sut = CreateSut(device, todayState, inner);

        var result = await sut.Handle(new TrayClockInCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        attendance.Verify(x => x.AddRecordAsync(
            It.Is<AttendanceRecord>(r => r.AttendanceSource == "tray"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AttendanceTodayContext BuildContext() => new(
        new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId, WorkModeId = 1 },
        new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo" },
        "Asia/Colombo",
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"),
        WorkDate,
        UtcNow,
        UtcNow,
        new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
        AttendanceRecord.WorkAreaRemote,
        "active_employee_work_mode",
        new ClockInPolicy { Id = Guid.NewGuid(), RemoteTrayEnabled = true },
        "configured",
        new AllowedClockInMethods(false, true, false, false, false, null),
        new AttendanceLocalDayWindow(UtcNow.AddHours(-8), UtcNow.AddHours(16)));

    private static AttendanceTodayResponse CreateTodayResponse()
        => new(
            EmployeeId,
            LegalEntityId,
            WorkDate,
            "Asia/Colombo",
            "configured",
            "configured",
            true,
            false,
            null,
            "09:00",
            "17:30",
            510,
            60,
            0,
            60,
            "not_started",
            "remote",
            AttendanceRecord.StatusOnTime,
            UtcNow,
            null,
            0,
            "tray",
            false,
            false,
            false,
            false,
            false,
            false,
            new AllowedClockInMethods(false, true, false, false, false, null),
            []);
}
