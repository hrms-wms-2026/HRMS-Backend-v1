using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Alerts;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class EmployeeAttendancePeriodReaderExcusedTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private static readonly EmployeePeriod August = new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

    public EmployeeAttendancePeriodReaderExcusedTests()
    {
        _attendance.Setup(a => a.ListRecordsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), August.From, August.To, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<AttendanceRecord>(), 0));
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 8, 21));
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"));
    }

    private EmployeeAttendancePeriodReader Reader(IAttendanceAlertResolutionLookup? lookup, IEmployeeClockInPolicyResolver? policies = null)
    {
        _attendance.Setup(a => a.ListBreaksAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BreakRecord>());
        var leaves = new Mock<ILeaveRequestReadRepository>();
        leaves.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), August.From, August.To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ONEVO.Domain.Features.Leave.Request.Entities.LeaveRequest>());
        var calendar = new Mock<ICalendarEventRepository>();
        calendar.Setup(c => c.ListHolidayDatesAsync(_tenantId, August.From, August.To, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DateOnly>());
        return new(_attendance.Object, leaves.Object, new Mock<ILegalEntityRepository>().Object,
            _clock.Object, new Mock<IEmployeeRepository>().Object, calendar.Object, lookup, policies);
    }

    [Fact]
    public async Task LoadAsync_CarriesTheExcusedDatesOfThePeriod()
    {
        IReadOnlyDictionary<string, IReadOnlySet<DateOnly>> excused = new Dictionary<string, IReadOnlySet<DateOnly>>
        {
            [AttendanceAlertSignals.AbsentDays] = new HashSet<DateOnly> { new(2026, 8, 10) }
        };
        var lookup = new Mock<IAttendanceAlertResolutionLookup>();
        lookup.Setup(l => l.ExcusedDatesAsync(_tenantId, _employeeId, August.From, August.To, It.IsAny<CancellationToken>())).ReturnsAsync(excused);

        var data = await Reader(lookup.Object).LoadAsync(_tenantId, _employeeId, null, August);

        data.Excused.Should().BeSameAs(excused);
    }

    [Fact]
    public async Task LoadAsync_WithoutALookup_ExcusesNothing()
    {
        var data = await Reader(null).LoadAsync(_tenantId, _employeeId, null, August);

        data.Excused.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_CarriesThePolicyThreshold()
    {
        var legalEntityId = Guid.NewGuid();
        var policy = new ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy();
        policy.LateDeductionRules.Add(new ONEVO.Domain.Features.TimeAttendance.Entities.ClockInLateDeductionRule { LateArrivalMinute = 15, Multiplier = 1m, IsActive = true });
        var policies = new Mock<IEmployeeClockInPolicyResolver>();
        policies.Setup(p => p.ResolveAsync(_tenantId, _employeeId, legalEntityId, August.To, It.IsAny<CancellationToken>())).ReturnsAsync(policy);

        var data = await Reader(null, policies.Object).LoadAsync(_tenantId, _employeeId, legalEntityId, August);

        data.LateEarlyThresholdMinutes.Should().Be(15);
    }

    [Fact]
    public async Task LoadAsync_WithoutAPolicy_ThresholdIsZero()
        => (await Reader(null).LoadAsync(_tenantId, _employeeId, Guid.NewGuid(), August)).LateEarlyThresholdMinutes.Should().Be(0);
}
