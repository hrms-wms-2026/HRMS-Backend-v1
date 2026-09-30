using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring;

public sealed class GetEmployeeActivityOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IActivityDailySummaryRepository> _summaries = new();
    private readonly Mock<IActivityLiveDaySummary> _live = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeActivityOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private GetEmployeeActivityOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _summaries.Object, _live.Object, _toggles.Object, _user.Object, _clock.Object);

    private ActivityDailySummary Day(int active, int idle, int meeting) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = SepFrom,
        TotalActiveMinutes = active, TotalIdleMinutes = idle, TotalMeetingMinutes = meeting
    };

    private void ArrangeSummaries(DateOnly from, DateOnly to, params ActivityDailySummary[] rows) =>
        _summaries.Setup(s => s.GetRangeAsync(_tenantId, _employeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Handle_DoesNotRequireAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(false);
        ArrangeSummaries(SepFrom, SepTo);

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SumsMinutesAndCountsDaysWithData()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(150, 72, 65), Day(200, 30, 0), Day(0, 0, 0));

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        var v = result.Value!;
        v.ActivityMonitoringEnabled.Should().BeTrue();
        v.ActiveMinutes.Should().Be(350);
        v.IdleMinutes.Should().Be(102);
        v.MeetingMinutes.Should().Be(65);
        v.DaysWithData.Should().Be(2);
        v.Previous.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsZeroesWithoutReadingSummaries_WhenMonitoringIsDisabled()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeFalse();
        result.Value.ActiveMinutes.Should().Be(0);
        result.Value.Previous.Should().BeNull();
        _summaries.Verify(s => s.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_ReadsThePreviousMonthToo()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(100, 10, 5));
        ArrangeSummaries(AugFrom, AugTo, Day(300, 40, 20), Day(100, 10, 0));

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.ActiveMinutes.Should().Be(100);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.ActiveMinutes.Should().Be(400);
        result.Value.Previous.IdleMinutes.Should().Be(50);
        result.Value.Previous.MeetingMinutes.Should().Be(20);
        result.Value.Previous.DaysWithData.Should().Be(2);
    }

    [Fact]
    public async Task Handle_AddsTheLiveSummaryForToday_WhenNoPersistedRowExistsYet()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(100, 20, 15));
        _live.Setup(l => l.ComposeAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummaryDto { TotalActiveMinutes = 30, TotalIdleMinutes = 5, TotalMeetingMinutes = 10 });

        var result = await CreateHandler().Handle(
            new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.Value!.ActiveMinutes.Should().Be(130);
        result.Value.IdleMinutes.Should().Be(25);
        result.Value.MeetingMinutes.Should().Be(25);
        result.Value.DaysWithData.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DoesNotComposeToday_ForAPastPeriod()
    {
        ArrangeSummaries(AugFrom, AugTo, Day(100, 20, 15));

        await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, AugFrom, AugTo), CancellationToken.None);

        _live.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValueOrInvalidPeriod()
    {
        (await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, null, null, "x"), CancellationToken.None))
            .StatusCode.Should().Be(400);
        (await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepTo, SepFrom), CancellationToken.None))
            .StatusCode.Should().Be(400);
    }
}
