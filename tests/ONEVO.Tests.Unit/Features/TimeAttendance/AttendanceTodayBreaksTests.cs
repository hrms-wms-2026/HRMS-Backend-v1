using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

/// <summary>
/// The Today response must carry the day's raw break intervals (not just the derived
/// BreakUsedMinutes/BreakState) so the dashboard's activity timeline can render a break
/// segment while the break is still open, not only after it is closed.
/// </summary>
public sealed class AttendanceTodayBreaksTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    private const int OnsiteWorkModeId = 1;

    [Fact]
    public async Task Today_IncludesAnOpenBreakInterval_WithNoEndedAt()
    {
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddMinutes(-10), BreakEnd = null
        };
        var service = CreateService([openBreak]);

        var result = await service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Breaks.Should().ContainSingle();
        result.Value.Breaks[0].StartedAt.Should().Be(openBreak.BreakStart);
        result.Value.Breaks[0].EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task Today_IncludesClosedAndOpenBreakIntervalsTogether()
    {
        var closedBreak = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddHours(-2), BreakEnd = UtcNow.AddHours(-2).AddMinutes(15)
        };
        var openBreak = new BreakRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            BreakStart = UtcNow.AddMinutes(-5), BreakEnd = null
        };
        var service = CreateService([closedBreak, openBreak]);

        var result = await service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Breaks.Should().HaveCount(2);
        result.Value.Breaks.Should().ContainSingle(b => b.StartedAt == closedBreak.BreakStart && b.EndedAt == closedBreak.BreakEnd);
        result.Value.Breaks.Should().ContainSingle(b => b.StartedAt == openBreak.BreakStart && b.EndedAt == null);
    }

    [Fact]
    public async Task Today_WithNoBreaksToday_ReturnsAnEmptyBreaksList()
    {
        var service = CreateService([]);

        var result = await service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Breaks.Should().BeEmpty();
    }

    private static AttendanceTodayStateService CreateService(IReadOnlyList<BreakRecord> breakRecords)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var employee = new Employee
        {
            Id = EmployeeId, UserId = UserId, TenantId = TenantId,
            LegalEntityId = LegalEntityId, WorkModeId = OnsiteWorkModeId
        };
        var legalEntity = new LegalEntity
        {
            Id = LegalEntityId, TenantId = TenantId, Timezone = "UTC",
            StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = new(9, 0), WorkEndTime = new(17, 0),
            BreakDurationMinutes = 60
        };
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);

        var policy = new ClockInPolicy
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1),
            OnsiteWebEnabled = true
        };
        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([policy]);

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                Date = WorkDate, ExpectedWorkingDay = true,
                ScheduledStart = new(9, 0), ScheduledEnd = new(17, 0),
                ExpectedWorkArea = AttendanceRecord.WorkAreaOnsite,
                ActualStart = UtcNow.AddHours(-3), Status = AttendanceRecord.StatusActive
            });
        attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breakRecords);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = OnsiteWorkModeId, Code = "onsite", Label = "On-site" }]);

        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupGet(x => x.UtcNow).Returns(UtcNow);

        var workAreaChangeRequests = new Mock<IWorkAreaChangeRequestRepository>();
        workAreaChangeRequests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkAreaChangeRequest?)null);

        var expectedWorkAreas = new ExpectedWorkAreaResolver(
            dateTime.Object, workModes.Object, workAreaChangeRequests.Object);

        return new AttendanceTodayStateService(
            currentUser.Object, dateTime.Object, employees.Object, legalEntities.Object,
            policies.Object, attendance.Object, authority.Object, expectedWorkAreas);
    }
}
