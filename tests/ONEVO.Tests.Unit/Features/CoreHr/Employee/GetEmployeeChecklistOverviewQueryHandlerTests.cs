using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeChecklistOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeChecklistOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeChecklistOverviewQueryHandler CreateHandler() => new(_guard.Object, _tasks.Object, _user.Object);

    private EmployeeChecklistTask Task(string lifecycle, string? category, string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
        LifecycleType = lifecycle, Category = category, Status = status, TaskTitle = "t"
    };

    private void Arrange(params EmployeeChecklistTask[] tasks) =>
        _tasks.Setup(t => t.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(tasks);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ReturnsNoGroups_WhenTheEmployeeHasNoChecklistTasks()
    {
        Arrange();

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.Value!.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_GroupsByCategory_AndCountsCompletedBypassedAndTotal()
    {
        Arrange(
            Task("onboarding", "Security training", "completed"),
            Task("onboarding", "Security training", "completed"),
            Task("onboarding", "Equipment setup", "completed"),
            Task("onboarding", "Equipment setup", "pending"),
            Task("onboarding", "Equipment setup", "bypassed"),
            Task("onboarding", "Equipment setup", "in_progress"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        var groups = result.Value!.Groups.ToDictionary(g => g.Name);
        groups["Security training"].Should().Be(new EmployeeChecklistGroup("Security training", "onboarding", 2, 0, 2));
        groups["Equipment setup"].Should().Be(new EmployeeChecklistGroup("Equipment setup", "onboarding", 1, 1, 4));
    }

    [Fact]
    public async Task Handle_FallsBackToTheLifecycleNameWhenTasksHaveNoCategory_AndKeepsLifecyclesApart()
    {
        Arrange(
            Task("onboarding", null, "completed"),
            Task("onboarding", "  ", "pending"),
            Task("offboarding", null, "completed"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        var groups = result.Value!.Groups;
        groups.Should().HaveCount(2);
        groups.Should().ContainSingle(g => g.Name == "Onboarding" && g.LifecycleType == "onboarding" && g.Completed == 1 && g.Total == 2);
        groups.Should().ContainSingle(g => g.Name == "Offboarding" && g.LifecycleType == "offboarding" && g.Completed == 1 && g.Total == 1);
    }

    [Fact]
    public async Task Handle_ListsOnboardingGroupsBeforeOffboardingGroups_ThenByName()
    {
        Arrange(
            Task("offboarding", "Exit interview", "pending"),
            Task("onboarding", "Zebra", "pending"),
            Task("onboarding", "Alpha", "pending"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.Value!.Groups.Select(g => g.Name).Should().Equal("Alpha", "Zebra", "Exit interview");
    }
}
