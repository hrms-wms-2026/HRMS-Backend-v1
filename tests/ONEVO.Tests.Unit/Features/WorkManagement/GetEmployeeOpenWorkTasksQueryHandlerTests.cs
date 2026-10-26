using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeOpenWorkTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeOpenWorkTasksQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeOpenWorkTasksQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private static EmployeeTaskPeriodRow Row(string title, string priority, string? due = null, int progress = 0) =>
        new(due is null ? null : DateOnly.Parse(due), null, progress, false, null, Guid.NewGuid(), title, Guid.NewGuid(), "Website",
            ShortId: "WEB-1", Priority: priority, StatusName: "Status", StatusColor: "#000000",
            ObjectiveId: Guid.NewGuid(), ObjectiveTitle: "Checkout module");

    private Task<Result<IReadOnlyList<EmployeeWorkTaskItem>>> Run() =>
        new GetEmployeeOpenWorkTasksQueryHandler(_guard.Object, _tasks.Object, _user.Object, _clock.Object)
            .Handle(new GetEmployeeOpenWorkTasksQuery(_employeeId), CancellationToken.None);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure_WithoutReadingTasks()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        (await Run()).StatusCode.Should().Be(403);
        _tasks.Verify(t => t.ListOpenForEmployeeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ListsEveryOpenTaskForThatEmployee_OverdueFirstThenPriority()
    {
        _tasks.Setup(t => t.ListOpenForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Row("No due date", "low"),
                Row("Far future", "critical", "2027-06-01"),
                Row("Started", "high", "2026-10-01", progress: 30),
                Row("Late", "medium", "2026-09-01")
            ]);

        var result = await Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(i => i.Title).Should().Equal("Late", "Far future", "No due date", "Started");
        result.Value!.Select(i => i.Bucket).Should().Equal("overdue", "not_started", "not_started", "in_progress");
        result.Value![0].DaysOverdue.Should().Be(14);
    }
}
