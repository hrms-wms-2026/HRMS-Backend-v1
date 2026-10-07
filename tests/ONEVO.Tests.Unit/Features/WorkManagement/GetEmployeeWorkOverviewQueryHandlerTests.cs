using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeWorkOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeWorkOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _tasks.Object, _user.Object, _clock.Object);

    private void ArrangeRows(params EmployeeTaskPeriodRow[] rows) =>
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static EmployeeTaskPeriodRow Row(string? due = null, string? completed = null, int progress = 0, bool done = false) =>
        new(due is null ? null : DateOnly.Parse(due), completed is null ? null : DateTimeOffset.Parse(completed), progress, done, null);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_DoesNotRequireAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        ArrangeRows();

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ComputesCountsAndRates_AsOfTheEarlierOfTodayAndPeriodEnd()
    {
        ArrangeRows(
            Row(due: "2026-09-10", completed: "2026-09-09T10:00:00+00:00", done: true),   // completed, on time
            Row(due: "2026-09-10", completed: "2026-09-12T10:00:00+00:00", done: true),   // completed, late
            Row(due: "2026-09-01", progress: 30),                                          // overdue (before today 15th)
            Row(due: "2026-09-20", progress: 30),                                          // in progress (after today)
            Row(due: "2026-09-25"));                                                       // not started

        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        var v = result.Value!;
        v.Assigned.Should().Be(5);
        v.Completed.Should().Be(2);
        v.Overdue.Should().Be(1);
        v.InProgress.Should().Be(1);
        v.NotStarted.Should().Be(1);
        v.CompletionRatePercent.Should().Be(40);
        v.OnTimeRatePercent.Should().Be(33);   // 1 on time of 2 completed + 1 overdue
    }

    [Fact]
    public async Task Handle_OnTimeRateCountsOverdueOpenTasksAsMissed()
    {
        ArrangeRows(
            Row(due: "2026-09-10", completed: "2026-09-09T10:00:00+00:00", done: true),   // the only completed task, on time
            Row(due: "2026-09-05", progress: 20),                                          // overdue
            Row(due: "2026-09-12"),                                                        // overdue
            Row(due: "2026-09-28"));                                                       // not due yet - not judged

        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        result.Value!.OnTimeRatePercent.Should().Be(33);   // 1 / (1 + 2), not 100
    }

    [Fact]
    public async Task Handle_UsesThePeriodEndAsTheReference_ForAPastPeriod()
    {
        ArrangeRows(Row(due: "2026-08-20", progress: 10));   // not done by 31 Aug -> overdue in August

        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        result.Value!.Overdue.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OnTimeRateIsNull_WhenNoTaskHasReachedItsDueDate()
    {
        ArrangeRows(Row(progress: 10));

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.OnTimeRatePercent.Should().BeNull();
        result.Value.CompletionRatePercent.Should().Be(0);
    }
}
