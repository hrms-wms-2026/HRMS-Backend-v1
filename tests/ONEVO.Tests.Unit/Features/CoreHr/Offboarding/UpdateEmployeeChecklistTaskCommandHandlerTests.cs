using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class UpdateEmployeeChecklistTaskCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _taskId = Guid.NewGuid();
    private readonly Mock<IEmployeeChecklistTaskRepository> _repository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeOffboardingCoverageGuard> _coverageGuard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    public UpdateEmployeeChecklistTaskCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _currentUser.Setup(c => c.UserId).Returns(_userId);
        _coverageGuard
            .Setup(g => g.EnsureCovered(_tenantId, _userId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result?)null);
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = Guid.NewGuid() });
    }

    private UpdateEmployeeChecklistTaskCommandHandler CreateHandler() =>
        new(_repository.Object, _currentUser.Object, _coverageGuard.Object, _employees.Object);

    private void HasEmployeesWrite(bool value) =>
        _currentUser.Setup(c => c.HasPermission("employees:write")).Returns(value);

    private EmployeeChecklistTask PendingTask(bool isRequired)
    {
        var task = new EmployeeChecklistTask
        {
            Id = _taskId, EmployeeId = _employeeId, Status = EmployeeChecklistTaskStatuses.Pending, IsRequired = isRequired
        };
        _repository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        return task;
    }

    [Fact]
    public async Task Handle_CompletedTask_ReturnsConflict()
    {
        _repository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = _taskId, EmployeeId = _employeeId, Status = EmployeeChecklistTaskStatuses.Completed });

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, Guid.NewGuid(), new DateOnly(2026, 12, 1), true), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_EmployeesWriteHolder_CanChangeAssigneeAndRequiredFlag()
    {
        HasEmployeesWrite(true);
        var newAssignee = Guid.NewGuid();
        var task = PendingTask(isRequired: true);

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, newAssignee, new DateOnly(2026, 12, 15), false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.AssignedToId.Should().Be(newAssignee);
        task.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ManagerWithoutEmployeesWrite_CanReassign()
    {
        HasEmployeesWrite(false);
        var newAssignee = Guid.NewGuid();
        var task = PendingTask(isRequired: true);

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, newAssignee, new DateOnly(2026, 12, 20), true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.AssignedToId.Should().Be(newAssignee);
        task.DueDate.Should().Be(new DateOnly(2026, 12, 20));
    }

    [Fact]
    public async Task Handle_ManagerWithoutEmployeesWrite_CannotChangeRequiredFlag()
    {
        HasEmployeesWrite(false);
        var task = PendingTask(isRequired: true);

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, Guid.NewGuid(), new DateOnly(2026, 12, 15), false), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        task.IsRequired.Should().BeTrue();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ManagerWithoutEmployeesWrite_CannotEditOwnRecord()
    {
        HasEmployeesWrite(false);
        var originalAssignee = Guid.NewGuid();
        var task = PendingTask(isRequired: true);
        task.AssignedToId = originalAssignee;
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = _userId });

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, Guid.NewGuid(), new DateOnly(2026, 12, 15), true), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        task.AssignedToId.Should().Be(originalAssignee);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerWithoutCoverage_ReturnsForbidden_AndChangesNothing()
    {
        HasEmployeesWrite(true);
        var originalAssignee = Guid.NewGuid();
        var task = PendingTask(isRequired: true);
        task.AssignedToId = originalAssignee;
        _coverageGuard
            .Setup(g => g.EnsureCovered(_tenantId, _userId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Forbidden("You do not have management coverage over this employee."));

        var result = await CreateHandler()
            .Handle(new UpdateEmployeeChecklistTaskCommand(_employeeId, _taskId, Guid.NewGuid(), new DateOnly(2026, 12, 15), false), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        task.AssignedToId.Should().Be(originalAssignee);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
