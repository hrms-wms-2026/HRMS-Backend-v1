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
/// Proves that a session left open from a prior day (the employee forgot to clock out) is
/// surfaced on the NEXT day's Today response, even though that day's own attendance row hasn't
/// been created yet - this is what lets the app prompt the employee before they clock in again.
/// </summary>
public sealed class AttendanceTodayMissingClockOutTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Today_StalePriorDayOpenSession_SurfacesMissingClockOutAttention()
    {
        var openRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            Date = new DateOnly(2026, 8, 19), ActualStart = UtcNow.AddHours(-38), ActualEnd = null,
            Status = AttendanceRecord.StatusActive
        };
        var fixture = CreateFixture(todayRecord: null, openRecordFromPriorDay: openRecord);

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttentionType.Should().Be("missing_clock_out");
        result.Value.AttentionSeverity.Should().Be("critical");
        result.Value.AttentionLabel.Should().Contain("Aug 19");
        result.Value.AttentionWorkDate.Should().Be(new DateOnly(2026, 8, 19));
    }

    [Fact]
    public async Task Today_RecentOvernightSessionStillWithinThreshold_DoesNotSurfaceMissingClockOut()
    {
        // Started at 11pm the prior day, it's now 9am - only 10 hours, well within a plausible
        // single overnight shift. Must not be flagged as forgotten.
        var openRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
            Date = new DateOnly(2026, 8, 20), ActualStart = UtcNow.AddHours(-10), ActualEnd = null,
            Status = AttendanceRecord.StatusActive
        };
        var fixture = CreateFixture(todayRecord: null, openRecordFromPriorDay: openRecord);

        var result = await fixture.Service.GetTodayAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.AttentionType.Should().NotBe("missing_clock_out");
        result.Value.AttentionWorkDate.Should().BeNull();
    }

    private static Fixture CreateFixture(
        AttendanceRecord? todayRecord,
        AttendanceRecord? openRecordFromPriorDay)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var employee = new Employee
        {
            Id = EmployeeId, UserId = UserId, TenantId = TenantId,
            LegalEntityId = LegalEntityId, WorkModeId = 1
        };
        var legalEntity = new LegalEntity
        {
            Id = LegalEntityId, TenantId = TenantId, Timezone = "UTC",
            StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = new(9, 0), WorkEndTime = new(17, 0),
            BreakDurationMinutes = 30
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
            ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1)
        };
        var policies = new Mock<IClockInPolicyRepository>();
        policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([policy]);

        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(todayRecord);
        attendance.Setup(x => x.GetAnyOpenRecordAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openRecordFromPriorDay);
        attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var workModes = new Mock<IWorkModeRepository>();
        workModes.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WorkMode { Id = 1, Code = "onsite", Label = "On-site" }]);

        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupGet(x => x.UtcNow).Returns(UtcNow);

        var workAreaChangeRequests = new Mock<IWorkAreaChangeRequestRepository>();
        workAreaChangeRequests.Setup(x => x.GetApprovedForDateAsync(
                TenantId, LegalEntityId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.TimeAttendance.Entities.WorkAreaChangeRequest?)null);

        var expectedWorkAreas = new ExpectedWorkAreaResolver(
            dateTime.Object, workModes.Object, workAreaChangeRequests.Object);

        var service = new AttendanceTodayStateService(
            currentUser.Object, dateTime.Object, employees.Object, legalEntities.Object,
            policies.Object, attendance.Object, authority.Object, expectedWorkAreas);

        return new Fixture(service);
    }

    private sealed record Fixture(AttendanceTodayStateService Service);
}
