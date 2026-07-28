using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class ClockInContextResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 7, 30, 0, TimeSpan.Zero);

    private readonly Mock<IAgentGatewayRepository> _agents = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ITimeAttendanceRepository> _attendance = new();
    private readonly Mock<IVerificationRepository> _verification = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    [Fact]
    public async Task Resolve_ApprovedOneDayRequest_OverridesScheduleWorkArea()
    {
        var setup = ConfigureEligibleContext(scheduleArea: "onsite");
        _attendance.Setup(repository => repository.GetApprovedWorkAreaChangeAsync(
                setup.Employee.Id,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkAreaChangeRequest
            {
                TenantId = setup.Agent.TenantId,
                EmployeeId = setup.Employee.Id,
                Date = new DateOnly(2026, 7, 27),
                RequestedWorkArea = "remote",
                Status = "approved"
            });
        _verification.Setup(repository => repository.GetActiveRemoteProfileAsync(
                setup.Employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRemoteProfile(setup));

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanClockIn);
        Assert.Equal("remote", result.Value.ExpectedWorkArea);
        Assert.Equal("approved_work_area_change", result.Value.WorkAreaSource);
    }

    [Fact]
    public async Task Resolve_Holiday_ReturnsSafeOffDayState()
    {
        var setup = ConfigureEligibleContext();
        _attendance.Setup(repository => repository.GetScheduleHolidayAsync(
                setup.Schedule.Id,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkScheduleHoliday
            {
                TenantId = setup.Agent.TenantId,
                WorkScheduleId = setup.Schedule.Id,
                Date = new DateOnly(2026, 7, 27),
                Name = "Company holiday"
            });

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanClockIn);
        Assert.True(result.Value.IsHoliday);
        Assert.Equal("holiday", result.Value.ReasonCode);
    }

    [Fact]
    public async Task Resolve_RemoteLocationRequiredWithoutProfile_BlocksSetup()
    {
        var setup = ConfigureEligibleContext(scheduleArea: "remote");
        _verification.Setup(repository => repository.GetActiveRemoteProfileAsync(
                setup.Employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeRemoteWorkProfile?)null);

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanClockIn);
        Assert.Equal("remote_location_setup_required", result.Value.ReasonCode);
    }

    [Fact]
    public async Task Resolve_TraySourceDisabledForArea_BlocksClockIn()
    {
        var setup = ConfigureEligibleContext(scheduleArea: "onsite");
        setup.Policy.OnsiteTrayEnabled = false;

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanClockIn);
        Assert.Equal("tray_clock_in_disabled", result.Value.ReasonCode);
    }

    [Fact]
    public async Task Resolve_PhotoRequiredWithoutApprovedReference_BlocksSetup()
    {
        var setup = ConfigureEligibleContext(scheduleArea: "onsite");
        setup.Policy.OnsitePhotoRequired = true;
        _verification.Setup(repository => repository.GetActiveReferencePhotoAsync(
                setup.Employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerificationReferencePhoto?)null);

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.PhotoRequired);
        Assert.False(result.Value.ReferenceReady);
        Assert.False(result.Value.CanClockIn);
        Assert.Equal("reference_photo_required", result.Value.ReasonCode);
    }

    [Fact]
    public async Task Resolve_OpenDeviceSession_ReturnsAlreadyClockedInState()
    {
        var setup = ConfigureEligibleContext();
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                setup.Agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceSession
            {
                TenantId = setup.Agent.TenantId,
                EmployeeId = setup.Employee.Id,
                DeviceId = setup.Agent.Id,
                SessionStart = Now.AddMinutes(-20)
            });

        var result = await CreateResolver().ResolveAsync(
            setup.Agent.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanClockIn);
        Assert.True(result.Value.IsClockedIn);
        Assert.Equal("already_clocked_in", result.Value.ReasonCode);
    }

    private ClockInContextResolver CreateResolver() => new(
        _agents.Object,
        _profiles.Object,
        _legalEntities.Object,
        _attendance.Object,
        _verification.Object,
        _clock.Object);

    private ContextSetup ConfigureEligibleContext(string scheduleArea = "onsite")
    {
        var tenantId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            LegalEntityId = Guid.NewGuid()
        };
        var agent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            DeviceId = $"device-{Guid.NewGuid():N}",
            Status = "active"
        };
        var legalEntity = new LegalEntity
        {
            Id = employee.LegalEntityId.Value,
            TenantId = tenantId,
            IsActive = true,
            Timezone = "UTC",
            OfficeLatitude = 13.0827m,
            OfficeLongitude = 80.2707m,
            OfficeAllowedRadiusMeters = 250
        };
        var schedule = new WorkSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntity.Id,
            Name = "Weekday",
            Timezone = "UTC",
            IsActive = true
        };
        var assignment = new ScheduleAssignment
        {
            TenantId = tenantId,
            LegalEntityId = legalEntity.Id,
            WorkScheduleId = schedule.Id,
            AssignmentType = "employee",
            EmployeeId = employee.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var scheduleDay = new WorkScheduleDay
        {
            TenantId = tenantId,
            WorkScheduleId = schedule.Id,
            DayOfWeek = 1,
            IsWorkingDay = true,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            RequiredWorkMinutes = 480,
            ExpectedWorkArea = scheduleArea
        };
        var policy = new ClockInPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntity.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            LocationVerificationRequired = true,
            OnsiteTrayEnabled = true,
            RemoteTrayEnabled = true,
            EitherTrayEnabled = true,
            FieldTrayEnabled = true
        };

        _clock.SetupGet(provider => provider.UtcNow).Returns(Now);
        _agents.Setup(repository => repository.GetAgentByIdAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _profiles.Setup(repository => repository.GetEmployeeByIdAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _profiles.Setup(repository => repository.GetWorkLocationSettingsAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeWorkLocationSettings
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                WorkMode = scheduleArea,
                WorkLocationVerificationEnabled = true
            });
        _legalEntities.Setup(repository => repository.GetByIdAsync(
                legalEntity.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);
        _attendance.Setup(repository => repository.ResolveScheduleAssignmentAsync(
                employee,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);
        _attendance.Setup(repository => repository.GetScheduleAsync(
                schedule.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendance.Setup(repository => repository.GetScheduleDayAsync(
                schedule.Id,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduleDay);
        _attendance.Setup(repository => repository.GetScheduleHolidayAsync(
                schedule.Id,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkScheduleHoliday?)null);
        _attendance.Setup(repository => repository.ResolveClockInPolicyAsync(
                employee,
                legalEntity.Id,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        _attendance.Setup(repository => repository.GetApprovedWorkAreaChangeAsync(
                employee.Id,
                new DateOnly(2026, 7, 27),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);
        _attendance.Setup(repository => repository.GetOpenDeviceSessionAsync(
                agent.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceSession?)null);
        _verification.Setup(repository => repository.GetActivePolicyAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationPolicy
            {
                TenantId = tenantId,
                IsActive = true,
                CameraPhotoVerificationEnabled = true
            });
        _verification.Setup(repository => repository.GetActiveReferencePhotoAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationReferencePhoto
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                IsActive = true,
                Status = "approved"
            });
        _verification.Setup(repository => repository.GetActiveRemoteProfileAsync(
                employee.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRemoteProfile(new ContextSetup(
                agent,
                employee,
                legalEntity,
                schedule,
                policy)));

        return new ContextSetup(agent, employee, legalEntity, schedule, policy);
    }

    private static EmployeeRemoteWorkProfile CreateRemoteProfile(ContextSetup setup) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = setup.Agent.TenantId,
        EmployeeId = setup.Employee.Id,
        Status = "active",
        CoarseLocationJson =
            """{"latitude":13.0827,"longitude":80.2707,"accuracy_meters":25}"""
    };

    private sealed record ContextSetup(
        RegisteredAgent Agent,
        Employee Employee,
        LegalEntity LegalEntity,
        WorkSchedule Schedule,
        ClockInPolicy Policy);
}
