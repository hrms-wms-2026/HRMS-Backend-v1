using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeHistoryQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _checklist = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _juniorId = Guid.NewGuid();
    private readonly Guid _seniorId = Guid.NewGuid();
    private readonly Guid _managerA = Guid.NewGuid();
    private readonly Guid _managerB = Guid.NewGuid();

    public GetEmployeeHistoryQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 30));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        ArrangeEmployee(new DateOnly(2024, 2, 1), null, null);
        ArrangeAssignments();
        _positions.Setup(p => p.GetByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Position { Id = _juniorId, TenantId = _tenantId, Name = "Junior Engineer" },
                new Position { Id = _seniorId, TenantId = _tenantId, Name = "Senior Engineer" }
            });
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        _checklist.Setup(c => c.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeChecklistTask>());
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [_managerA] = "Grace Hopper", [_managerB] = "Abitha Devendran" });
    }

    private GetEmployeeHistoryQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _assignments.Object, _positions.Object, _leave.Object,
            _checklist.Object, _identity.Object, _user.Object, _clock.Object);

    private void ArrangeEmployee(DateOnly hire, DateOnly? probationEnd, DateOnly? termination) =>
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity
            {
                Id = _employeeId, TenantId = _tenantId, HireDate = hire, ProbationEndDate = probationEnd, TerminationDate = termination
            });

    private PositionAssignmentEntity Assignment(Guid positionId, string from, Guid? reportsTo, string? reason = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, PositionId = positionId,
        EffectiveFrom = DateOnly.Parse(from), ReportsToEmployeeId = reportsTo, ChangeReason = reason
    };

    private void ArrangeAssignments(params PositionAssignmentEntity[] items) =>
        _assignments.Setup(a => a.ListHistoryForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Handle_Returns400_ForAnOutOfRangeLimit(int limit)
    {
        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, limit), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ContainsTheJoinEvent_WithTheFirstPositionWhenKnown()
    {
        ArrangeAssignments(Assignment(_juniorId, "2024-02-01", _managerA));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var joined = result.Value!.Events.Single(e => e.Kind == "joined");
        joined.Title.Should().Be("Joined company");
        joined.Detail.Should().Be("Joined as Junior Engineer");
        joined.Date.Should().Be(new DateOnly(2024, 2, 1));
    }

    [Fact]
    public async Task Handle_EmitsPositionAndManagerChangesFromConsecutiveAssignments()
    {
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerB, "Promotion"));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var position = result.Value!.Events.Single(e => e.Kind == "position_changed");
        position.Title.Should().Be("Position changed");
        position.Detail.Should().Be("Junior Engineer → Senior Engineer (Promotion)");
        position.Date.Should().Be(new DateOnly(2025, 8, 15));

        var manager = result.Value.Events.Single(e => e.Kind == "manager_changed");
        manager.Title.Should().Be("Reporting manager changed");
        manager.Detail.Should().Be("Reporting manager changed to Abitha Devendran");
        manager.Date.Should().Be(new DateOnly(2025, 8, 15));
    }

    [Fact]
    public async Task Handle_EmitsNoManagerChange_WhenTheManagerStaysTheSameOrIsUnknown()
    {
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerA),
            Assignment(_seniorId, "2026-01-01", null));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.Value!.Events.Should().NotContain(e => e.Kind == "manager_changed");
        result.Value.Events.Count(e => e.Kind == "position_changed").Should().Be(2);
    }

    [Fact]
    public async Task Handle_EmitsProbationCompletedOnlyOnceTheDateHasPassed()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), new DateOnly(2024, 5, 1), null);
        (await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None))
            .Value!.Events.Should().ContainSingle(e => e.Kind == "probation_completed" && e.Date == new DateOnly(2024, 5, 1));

        ArrangeEmployee(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), null);
        (await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None))
            .Value!.Events.Should().NotContain(e => e.Kind == "probation_completed");
    }

    [Fact]
    public async Task Handle_EmitsTheEndOfEmploymentWhenTerminated()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), null, new DateOnly(2026, 9, 1));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.Value!.Events.Should().ContainSingle(e => e.Kind == "terminated" && e.Title == "Employment ended" && e.Date == new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Handle_EmitsApprovedLeaveAndCompletedChecklistTasks_AndIgnoresTheRest()
    {
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "approved", TotalHours = 24, ApprovedAt = DateTimeOffset.Parse("2026-08-28T10:00:00+00:00") }, "Annual leave", "AL"),
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "pending", TotalHours = 8 }, "Sick leave", "SL"),
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "approved", TotalHours = 8, ApprovedAt = null }, "Sick leave", "SL")
            });
        _checklist.Setup(c => c.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmployeeChecklistTask { Id = Guid.NewGuid(), LifecycleType = "onboarding", Category = "Security training", TaskTitle = "Complete course", Status = "completed", CompletedAt = DateTimeOffset.Parse("2026-08-15T09:00:00+00:00") },
                new EmployeeChecklistTask { Id = Guid.NewGuid(), LifecycleType = "onboarding", Category = "Security training", TaskTitle = "Pending one", Status = "pending" }
            });

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var leave = result.Value!.Events.Single(e => e.Kind == "leave_approved");
        leave.Title.Should().Be("Leave approved");
        leave.Detail.Should().Be("Annual leave (24 h)");
        leave.Date.Should().Be(new DateOnly(2026, 8, 28));
        var checklist = result.Value.Events.Single(e => e.Kind == "checklist_completed");
        checklist.Title.Should().Be("Checklist task completed");
        checklist.Detail.Should().Be("Security training: Complete course");
        checklist.Date.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task Handle_ReturnsNewestFirst_AndHonoursTheLimit()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), new DateOnly(2024, 5, 1), new DateOnly(2026, 9, 1));
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerB));

        var all = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, 50), CancellationToken.None);
        all.Value!.Events.Select(e => e.Date).Should().BeInDescendingOrder();
        all.Value.Events.First().Kind.Should().Be("terminated");

        var limited = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, 2), CancellationToken.None);
        limited.Value!.Events.Should().HaveCount(2);
        limited.Value.Events.Select(e => e.Date).Should().Equal(all.Value.Events.Take(2).Select(e => e.Date));
    }
}
