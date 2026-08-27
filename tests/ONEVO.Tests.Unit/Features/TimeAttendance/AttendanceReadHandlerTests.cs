using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceReadHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task Today_AfterStartWithoutRecord_SetsShouldHaveClockedIn()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:00:00+00:00");
        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ShouldHaveClockedIn.Should().BeTrue();
        result.Value.AttendanceStatus.Should().Be("not_clocked_in");
    }

    [Fact]
    public async Task Today_AfterStartWithRecordWithoutClockIn_SetsShouldHaveClockedIn()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:00:00+00:00");
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21), ExpectedWorkingDay = true });

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.ShouldHaveClockedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Today_BeforeStartOrOffDay_DoesNotSetShouldHaveClockedIn()
    {
        var beforeStart = CreateFixture(localTimeUtc: "2026-08-21T03:00:00+00:00");
        (await beforeStart.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None)).Value!.ShouldHaveClockedIn.Should().BeFalse();

        var sunday = CreateFixture(localTimeUtc: "2026-08-23T10:00:00+00:00");
        (await sunday.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None)).Value!.IsWorkingDay.Should().BeFalse();
        (await sunday.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None)).Value!.ShouldHaveClockedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Today_UsesLegalEntityLocalDayWindowForBreaks()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:00:00+00:00");
        await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        var expectedStart = new DateTimeOffset(2026, 8, 20, 18, 30, 0, TimeSpan.Zero);
        var expectedEnd = new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.Zero);
        fixture.Attendance.Verify(x => x.ListBreaksAsync(TenantId, EmployeeId, expectedStart, expectedEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Today_MapsOnsitePolicyFieldsAndDoesNotUseEmploymentType()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:00:00+00:00", workModeCode: "onsite", employmentTypeId: 99);
        fixture.Policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClockInPolicy { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1), OnsiteWebEnabled = true, RemoteWebEnabled = false }]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.AllowedClockInMethods.Web.Should().BeTrue();
        result.Value.AllowedClockInMethods.DesktopTray.Should().BeFalse();
    }

    [Fact]
    public async Task Today_MissingPolicyDisablesClockInWithStableMessage()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:00:00+00:00");
        fixture.Policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.CanClockIn.Should().BeFalse();
        result.Value.Messages.Should().Contain("clock_in_policy_not_configured");
    }

    [Fact]
    public async Task CoveredHistory_WithEmployeeFilterQueriesOnlyThatVisibleEmployeeAndPreservesIdentity()
    {
        var fixture = CreateFixture();
        var selectedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, selectedId, otherId]));
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = selectedId, Date = new(2026, 8, 21), Status = "late" };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { selectedId })), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((new List<AttendanceRecord> { record }, 1));
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [selectedId] = new(selectedId, "Jane Doe", "EMP-001", "Engineer", "Product", Guid.NewGuid()) });

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), selectedId, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].Employee!.DisplayName.Should().Be("Jane Doe");
        result.Value.Items[0].Employee!.EmployeeNumber.Should().Be("EMP-001");
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task CoveredHistory_OutsideVisibilityReturnsForbidden()
    {
        var fixture = CreateFixture();
        var hiddenId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), hiddenId, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        fixture.Attendance.Verify(x => x.ListRecordsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Today_ActiveAttendanceWithAllowance_AllowsStartBreak()
    {
        var fixture = CreateFixture();
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00") });

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.CanStartBreak.Should().BeTrue();
        result.Value.BreakRemainingMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Today_ExhaustedAllowance_DisablesStartBreak()
    {
        var fixture = CreateFixture();
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00") });
        fixture.Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"), BreakEnd = DateTimeOffset.Parse("2026-08-21T05:00:00+00:00") }]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.BreakRemainingMinutes.Should().Be(0);
        result.Value.CanStartBreak.Should().BeFalse();
        result.Value.Messages.Should().Contain("break_allowance_used");
    }

    [Fact]
    public async Task Today_OpenBreak_AllowsEndOnly()
    {
        var fixture = CreateFixture();
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00") });
        fixture.Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T09:00:00+00:00") }]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.CanEndBreak.Should().BeTrue();
        result.Value.CanStartBreak.Should().BeFalse();
    }

    [Fact]
    public async Task Today_WhileClockedIn_ComputesLiveWorkedMinutesInsteadOfStoredZero()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T12:00:00+00:00");
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, ActualStart = DateTimeOffset.Parse("2026-08-21T10:00:00+00:00"), WorkedMinutes = 0 });

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.TotalWorkedMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Today_WhileOnOpenBreak_StatusIsOnBreakNotWorking()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T12:00:00+00:00");
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, ActualStart = DateTimeOffset.Parse("2026-08-21T10:00:00+00:00") });
        fixture.Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T11:55:00+00:00") }]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.AttendanceStatus.Should().Be("on_break");
        result.Value.AttendanceStatusLabel.Should().Be("On break");
    }

    [Fact]
    public async Task MyHistory_TodayRowWithOpenBreak_DoesNotInflateOverageToEndOfDay()
    {
        var fixture = CreateFixture(localTimeUtc: "2026-08-21T10:05:00+00:00");
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = new(2026, 8, 21),
            ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00")
        };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { EmployeeId })), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AttendanceRecord> { record }, 1));
        fixture.Attendance.Setup(x => x.ListBreaksForEmployeesAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T10:00:00+00:00") }]);

        var result = await fixture.Handler.Handle(
            new GetMyAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), new PagedRequest()), CancellationToken.None);

        result.Value!.Items[0].IsOverBreakAllowance.Should().BeFalse();
        result.Value.Items[0].TotalWorkedMinutes.Should().Be(360);
    }

    [Fact]
    public async Task Today_NoBreakAllowance_ReturnsNullRemainingAndDisablesStart()
    {
        var fixture = CreateFixture();
        fixture.LegalEntity.BreakDurationMinutes = null;
        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.BreakAllowanceMinutes.Should().BeNull();
        result.Value.BreakRemainingMinutes.Should().BeNull();
        result.Value.CanStartBreak.Should().BeFalse();
        result.Value.Messages.Should().Contain("break_allowance_not_configured");
    }

    [Theory]
    [InlineData("onsite")]
    [InlineData("remote")]
    [InlineData("hybrid")]
    [InlineData("field")]
    public async Task Today_MapsAllowedMethodsFromWorkMode(string mode)
    {
        var fixture = CreateFixture(workModeCode: mode);
        var policy = new ClockInPolicy { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1) };
        if (mode == "onsite") policy.OnsiteWebEnabled = true;
        else if (mode == "remote") policy.RemoteWebEnabled = true;
        else if (mode == "hybrid") policy.EitherWebEnabled = true;
        else policy.FieldWebEnabled = true;
        fixture.Policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>())).ReturnsAsync([policy]);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.AllowedClockInMethods.Web.Should().BeTrue();
        result.Value.ExpectedWorkMode.Should().Be(mode);
    }

    [Fact]
    public async Task Today_MultipleActivePolicies_DisablesClockInWithConflictMessage()
    {
        var fixture = CreateFixture();
        var policies = Enumerable.Range(1, 2).Select(_ => new ClockInPolicy { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1) }).ToList();
        fixture.Policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>())).ReturnsAsync(policies);

        var result = await fixture.Handler.Handle(new GetAttendanceTodayQuery(), CancellationToken.None);

        result.Value!.CanClockIn.Should().BeFalse();
        result.Value.Messages.Should().Contain("multiple_active_company_policies");
    }

    [Fact]
    public async Task CoveredHistory_WithoutEmployeeFilter_QueriesAllResolverVisibleEmployees()
    {
        var fixture = CreateFixture();
        var secondId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, secondId]));
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((new List<AttendanceRecord>(), 0));

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), null, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Attendance.Verify(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MyHistory_AppliesPagingAndReturnsPagedResult()
    {
        var fixture = CreateFixture();
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21) };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { EmployeeId })), new(2026, 8, 1), new(2026, 8, 21), 20, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AttendanceRecord> { record }, 45));

        var result = await fixture.Handler.Handle(
            new GetMyAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), new PagedRequest { PageNumber = 2, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.PageNumber.Should().Be(2);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalCount.Should().Be(45);
        result.Value.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task DayDetail_Self_ReturnsSummaryTimelineAndActivityRegardlessOfPermissions()
    {
        var fixture = CreateFixture();
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            Date = new(2026, 8, 21),
            ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"),
            ActualEnd = DateTimeOffset.Parse("2026-08-21T12:30:00+00:00"),
            AttendanceSource = "web"
        };
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        fixture.Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T08:00:00+00:00"), BreakEnd = DateTimeOffset.Parse("2026-08-21T08:15:00+00:00") }]);
        fixture.ActivitySummaries.Setup(x => x.GetAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummary { TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21), TotalActiveMinutes = 200, TotalIdleMinutes = 40 });

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Summary.AttendanceRecordId.Should().Be(record.Id);
        result.Value.TimelineEvents.Should().HaveCount(4);
        result.Value.TimelineEvents[0].EventType.Should().Be("ClockIn");
        result.Value.TimelineEvents[1].EventType.Should().Be("BreakStart");
        result.Value.TimelineEvents[2].EventType.Should().Be("BreakEnd");
        result.Value.TimelineEvents[3].EventType.Should().Be("ClockOut");
        result.Value.DailyActivity.Should().NotBeNull();
        result.Value.DailyActivity!.TotalActiveMinutes.Should().Be(200);
    }

    [Fact]
    public async Task DayDetail_OtherEmployee_WithAttendanceReadAndMonitoringRead_ReturnsActivity()
    {
        var fixture = CreateFixture(hasMonitoringRead: true);
        var otherId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, otherId]));
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21) };
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [otherId] = new(otherId, "Jane Doe", "EMP-001", "Engineer", "Product", null) });
        fixture.ActivitySummaries.Setup(x => x.GetAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityDailySummary { TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21), TotalActiveMinutes = 150 });

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(otherId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Summary.Employee!.DisplayName.Should().Be("Jane Doe");
        result.Value.DailyActivity.Should().NotBeNull();
        result.Value.DailyActivity!.TotalActiveMinutes.Should().Be(150);
    }

    [Fact]
    public async Task DayDetail_OtherEmployee_WithAttendanceReadOnly_NullsActivityWithoutForbidden()
    {
        var fixture = CreateFixture();
        var otherId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, otherId]));
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21) };
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [otherId] = new(otherId, "Jane Doe", "EMP-001", "Engineer", "Product", null) });

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(otherId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DailyActivity.Should().BeNull();
        fixture.ActivitySummaries.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DayDetail_OutsideVisibility_ReturnsForbidden()
    {
        var fixture = CreateFixture();
        var hiddenId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(hiddenId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        fixture.Attendance.Verify(x => x.GetRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DayDetail_NoAttendanceRecordForDate_ReturnsNotFound()
    {
        var fixture = CreateFixture();
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceRecord?)null);

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DayDetail_NoActivitySummaryRow_ReturnsNullActivityNotError()
    {
        var fixture = CreateFixture();
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21) };
        fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DailyActivity.Should().BeNull();
    }

    private static Fixture CreateFixture(string localTimeUtc = "2026-08-21T10:00:00+00:00", string workModeCode = "remote", int employmentTypeId = 1, bool hasMonitoringRead = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        currentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
        currentUser.Setup(x => x.HasPermission("monitoring:read")).Returns(hasMonitoringRead);

        var employee = new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId, WorkModeId = 1, EmploymentTypeId = employmentTypeId };
        var legalEntity = new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo", StandardWorkingDays = "[1,2,3,4,5]", WorkStartTime = new(9, 0), WorkEndTime = new(17, 30), BreakDurationMinutes = 60 };
        var employees = new Mock<IEmployeeRepository>(); employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var legalEntities = new Mock<ILegalEntityRepository>(); legalEntities.Setup(x => x.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(legalEntity);
        var policies = new Mock<IClockInPolicyRepository>(); policies.Setup(x => x.ListByLegalEntityAsync(TenantId, LegalEntityId, false, It.IsAny<CancellationToken>())).ReturnsAsync([new ClockInPolicy { Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, ScopeType = ClockInPolicy.ScopeFullCompany, EffectiveFrom = new(2026, 1, 1), RemoteWebEnabled = true }]);
        var attendance = new Mock<IAttendanceReadRepository>(); attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceRecord?)null); attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var authority = new Mock<IEmployeeAuthorityResolver>(); authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));
        var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();
        var activitySummaries = new Mock<IActivityDailySummaryRepository>();
        activitySummaries.Setup(x => x.GetAsync(TenantId, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityDailySummary?)null);
        expectedWorkAreas.Setup(x => x.ResolveAsync(It.IsAny<Employee>(), It.IsAny<LegalEntity>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExpectedWorkAreaResolution>.Success(
                new ExpectedWorkAreaResolution(ToExpectedWorkArea(workModeCode), legalEntity.Timezone!, "active_employee_work_mode")));
        var dateTime = new Mock<IDateTimeProvider>(); dateTime.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.Parse(localTimeUtc));
        var todayState = new ONEVO.Application.Features.TimeAttendance.Services.AttendanceTodayStateService(
            currentUser.Object,
            dateTime.Object,
            employees.Object,
            legalEntities.Object,
            policies.Object,
            attendance.Object,
            authority.Object,
            expectedWorkAreas.Object);
        return new Fixture(
            new AttendanceReadHandler(
                currentUser.Object,
                employees.Object,
                attendance.Object,
                authority.Object,
                todayState,
                legalEntities: legalEntities.Object,
                dateTimeProvider: dateTime.Object,
                activitySummaries: activitySummaries.Object),
            attendance,
            policies,
            authority,
            legalEntity,
            activitySummaries);
    }

    private static string ToExpectedWorkArea(string workModeCode) => workModeCode switch
    {
        "hybrid" => "either",
        _ => workModeCode
    };

    private sealed record Fixture(AttendanceReadHandler Handler, Mock<IAttendanceReadRepository> Attendance, Mock<IClockInPolicyRepository> Policies, Mock<IEmployeeAuthorityResolver> Authority, LegalEntity LegalEntity, Mock<IActivityDailySummaryRepository> ActivitySummaries);
}
