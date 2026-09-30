using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkActivityTaskHandlersTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 15);

    public GetEmployeeWorkActivityTaskHandlersTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, Guid.NewGuid(), null, "full_time", "active", null, null)));
    }

    private EmployeeWorkTaskRow Row(string shortId, string due) => new(
        Guid.NewGuid(), shortId, shortId, Guid.NewGuid(), "Website", "Review", "#7C3AED", false,
        "high", 5, DateOnly.Parse(due), 60, DateTimeOffset.Parse("2026-09-10T00:00:00+00:00"));

    [Fact]
    public async Task NeedsAttention_ReadsTasksDueWithinThreeDays_AndShapesThem()
    {
        _tasks.Setup(t => t.ListOpenDueByAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 18), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("A", "2026-09-17"), Row("B", "2026-09-13") });
        var handler = new GetEmployeeNeedsAttentionQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeNeedsAttentionQuery(_employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Select(i => i.ShortId).Should().Equal("B", "A");
        result.Value.Items[0].StatusName.Should().Be("Review");
    }

    [Fact]
    public async Task NeedsAttention_PassesThroughGuardFailure_WithoutReadingTasks()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));
        var handler = new GetEmployeeNeedsAttentionQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeNeedsAttentionQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _tasks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecentTasks_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));
        var handler = new GetEmployeeRecentTasksQueryHandler(_guard.Object, _tasks.Object, _user.Object);

        var result = await handler.Handle(new GetEmployeeRecentTasksQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RecentTasks_ReadsAndShapesTheTopFive()
    {
        _tasks.Setup(t => t.ListRecentlyChangedAssignedAsync(_tenantId, _employeeId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("A", "2026-09-20") });
        var handler = new GetEmployeeRecentTasksQueryHandler(_guard.Object, _tasks.Object, _user.Object);

        var result = await handler.Handle(new GetEmployeeRecentTasksQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.Priority.Should().Be("high");
    }

    [Fact]
    public async Task DeliveryTrend_DefaultsToTheCurrentMonth_AndReadsASixMonthWindow()
    {
        _tasks.Setup(t => t.ListCompletedAtForEmployeeAsync(_tenantId, _employeeId,
                DateTimeOffset.Parse("2026-04-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { DateTimeOffset.Parse("2026-09-03T08:00:00+00:00") });
        var handler = new GetEmployeeDeliveryTrendQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeDeliveryTrendQuery(_employeeId, null), CancellationToken.None);

        result.Value!.Months.Should().HaveCount(6);
        result.Value.Months[^1].Should().Be(new EmployeeDeliveryTrendMonth("2026-09", 1));
    }
}
