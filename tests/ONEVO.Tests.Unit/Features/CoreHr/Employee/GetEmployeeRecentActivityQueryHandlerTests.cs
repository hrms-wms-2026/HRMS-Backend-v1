using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeRecentActivityQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeActivityFeedRepository> _feed = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();

    public GetEmployeeRecentActivityQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = _employeeUserId });
        Arrange();
    }

    private GetEmployeeRecentActivityQueryHandler CreateHandler() => new(_guard.Object, _employees.Object, _feed.Object, _user.Object);

    private static EmployeeActivityRow Row(string kind, string at, string? target = null, string? detail = null) =>
        new(kind, Guid.NewGuid(), DateTimeOffset.Parse(at), target, detail);

    private void Arrange(params EmployeeActivityRow[] rows) =>
        _feed.Setup(f => f.ListAsync(_tenantId, _employeeId, _employeeUserId, It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Handle_Returns400_ForAnOutOfRangeLimit(int limit)
    {
        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, limit), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_AsksTheFeedForOneMoreThanTheLimit_UsingTheEmployeesUserId_AndTheCursor()
    {
        var before = DateTimeOffset.Parse("2026-09-20T00:00:00+00:00");

        await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, before, 20), CancellationToken.None);

        _feed.Verify(f => f.ListAsync(_tenantId, _employeeId, _employeeUserId, before, 21, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TurnsRowsIntoWordedItems_KeepingTargetsAndDetails()
    {
        Arrange(
            Row("attendance_clock_in", "2026-09-21T03:30:00+00:00"),
            Row("task_status_changed", "2026-09-20T10:00:00+00:00", "WEB-12 Fix cart", "→ In review"),
            Row("leave_requested", "2026-09-19T09:00:00+00:00", "Annual leave"),
            Row("module_joined", "2026-09-18T09:00:00+00:00", "Checkout"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        var items = result.Value!.Items;
        items.Select(i => i.Action).Should().Equal("Clocked in", "Changed task status", "Requested leave", "Joined");
        items[1].Target.Should().Be("WEB-12 Fix cart");
        items[1].Detail.Should().Be("→ In review");
        items[0].Id.Should().StartWith("attendance_clock_in:");
    }

    [Theory]
    [InlineData("attendance_clock_out", "Clocked out")]
    [InlineData("attendance_correction_requested", "Requested an attendance correction")]
    [InlineData("work_area_change_requested", "Requested a work area change")]
    [InlineData("location_change_requested", "Requested a location change")]
    [InlineData("task_created", "Created task")]
    [InlineData("task_edited", "Edited task")]
    [InlineData("task_progress_changed", "Updated task progress")]
    [InlineData("task_commented", "Commented on task")]
    [InlineData("task_clocked_in", "Started working on task")]
    [InlineData("task_clocked_out", "Stopped working on task")]
    public async Task Handle_HasWordingForEveryKind(string kind, string expected)
    {
        Arrange(Row(kind, "2026-09-21T03:30:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Single().Action.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_FallsBackToAGenericActionForAnUnknownKind()
    {
        Arrange(Row("something_new", "2026-09-21T03:30:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Single().Action.Should().Be("Activity");
    }

    [Fact]
    public async Task Handle_TrimsTheExtraRow_AndReturnsACursorWhenMoreExist()
    {
        Arrange(Enumerable.Range(1, 3).Select(i => Row("task_edited", $"2026-09-2{i}T00:00:00+00:00")).Reverse().ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, 2), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(2);
        result.Value.NextBefore.Should().Be(result.Value.Items.Last().At);
    }

    [Fact]
    public async Task Handle_HasNoCursorWhenEverythingFits_AndIsAlwaysPartial()
    {
        Arrange(Row("task_edited", "2026-09-21T00:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, 2), CancellationToken.None);

        result.Value!.NextBefore.Should().BeNull();
        result.Value.IsPartial.Should().BeTrue();
    }
}
