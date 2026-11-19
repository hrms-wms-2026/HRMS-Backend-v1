using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkTasksQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);

    public GetEmployeeWorkTasksQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private void ArrangeRows(params EmployeeTaskPeriodRow[] rows) =>
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, SepFrom, SepTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static EmployeeTaskPeriodRow Row(
        string title, string priority, string? due = null, int progress = 0, bool complete = false, string? completedAt = null) =>
        new(due is null ? null : DateOnly.Parse(due), completedAt is null ? null : DateTimeOffset.Parse(completedAt),
            progress, complete, null, Guid.NewGuid(), title, Guid.NewGuid(), "Website",
            ShortId: "WEB-1", Priority: priority, StatusName: "Status", StatusColor: "#000000",
            ObjectiveId: Guid.NewGuid(), ObjectiveTitle: "Checkout module");

    private Task<Result<ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs.EmployeeWorkTasksResponse>> Run() =>
        new GetEmployeeWorkTasksQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object)
            .Handle(new GetEmployeeWorkTasksQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        (await Run()).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_OrdersByBucketThenPriorityThenDueDate()
    {
        ArrangeRows(
            Row("Done task", "critical", "2026-09-05", 100, complete: true, completedAt: "2026-09-04T10:00:00+00:00"),
            Row("Started low", "low", "2026-09-20", progress: 40),
            Row("Started critical", "critical", "2026-09-25", progress: 10),
            Row("Not started medium, no due", "medium"),
            Row("Not started medium, due", "medium", "2026-09-28"),
            Row("Not started high", "high", "2026-09-29"),
            Row("Late low", "low", "2026-09-10"),
            Row("Late high", "high", "2026-09-12"));

        var items = (await Run()).Value!.Items;

        items.Select(i => i.Title).Should().Equal(
            "Late high", "Late low",
            "Not started high", "Not started medium, due", "Not started medium, no due",
            "Started critical", "Started low",
            "Done task");
        items.Select(i => i.Bucket).Should().Equal(
            "overdue", "overdue", "not_started", "not_started", "not_started", "in_progress", "in_progress", "completed");
    }

    [Fact]
    public async Task Handle_CarriesTheDisplayFields_AndDaysOverdue()
    {
        ArrangeRows(Row("Late high", "high", "2026-09-12"));

        var item = (await Run()).Value!.Items.Should().ContainSingle().Subject;

        item.Priority.Should().Be("high");
        item.ProjectName.Should().Be("Website");
        item.ModuleName.Should().Be("Checkout module");
        item.StatusName.Should().Be("Status");
        item.ShortId.Should().Be("WEB-1");
        item.DueDate.Should().Be(new DateOnly(2026, 9, 12));
        item.DaysOverdue.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ListAddsUpToTheWorkCardCounts()
    {
        ArrangeRows(
            Row("a", "low", "2026-09-10"),
            Row("b", "high", "2026-09-20", progress: 50),
            Row("c", "medium"),
            Row("d", "medium", "2026-09-05", 100, complete: true, completedAt: "2026-09-04T10:00:00+00:00"));

        var list = (await Run()).Value!.Items;
        var counts = (await new GetEmployeeWorkOverviewQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object)
            .Handle(new GetEmployeeWorkOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None)).Value!;

        list.Count(i => i.Bucket == "overdue").Should().Be(counts.Overdue);
        list.Count(i => i.Bucket == "in_progress").Should().Be(counts.InProgress);
        list.Count(i => i.Bucket == "not_started").Should().Be(counts.NotStarted);
        list.Count(i => i.Bucket == "completed").Should().Be(counts.Completed);
    }
}
