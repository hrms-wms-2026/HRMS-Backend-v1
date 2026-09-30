using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeDeliveryQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeDeliveryQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeDeliveryQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

    private void ArrangeRows(DateOnly from, DateOnly to, params EmployeeTaskPeriodRow[] rows) =>
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static EmployeeTaskPeriodRow Done(string due, string completed, int? points = null) =>
        new(DateOnly.Parse(due), DateTimeOffset.Parse(completed), 100, true, points);

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksTasksReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ReturnsTheCurrentPeriodMetrics_AndNoPreviousByDefault()
    {
        ArrangeRows(SepFrom, SepTo,
            Done("2026-09-10", "2026-09-09T10:00:00+00:00", 5),
            Done("2026-09-10", "2026-09-12T10:00:00+00:00", 3),
            new EmployeeTaskPeriodRow(new DateOnly(2026, 9, 25), null, 0, false, 8));

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        var v = result.Value!;
        v.TasksAssigned.Should().Be(3);
        v.TasksCompleted.Should().Be(2);
        v.OnTimeCompleted.Should().Be(1);
        v.CompletedWithDueDate.Should().Be(2);
        v.StoryPointsAssigned.Should().Be(16);
        v.StoryPointsCompleted.Should().Be(8);
        v.Previous.Should().BeNull();
        _tasks.Verify(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, AugFrom, AugTo, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_AddsThePreviousMonthsMetrics()
    {
        ArrangeRows(SepFrom, SepTo, Done("2026-09-10", "2026-09-09T10:00:00+00:00", 5));
        ArrangeRows(AugFrom, AugTo,
            Done("2026-08-10", "2026-08-09T10:00:00+00:00", 2),
            Done("2026-08-12", "2026-08-11T10:00:00+00:00", 2));

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.TasksCompleted.Should().Be(1);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.TasksCompleted.Should().Be(2);
        result.Value.Previous.StoryPointsCompleted.Should().Be(4);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValue()
    {
        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, null, null, "last-year"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
