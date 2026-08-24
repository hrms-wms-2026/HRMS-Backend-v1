using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class UpdateEmployeeChecklistTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_CompletedTask_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var repository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Completed });

        var result = await new UpdateEmployeeChecklistTaskCommandHandler(repository.Object, currentUser.Object)
            .Handle(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, Guid.NewGuid(), new DateOnly(2026, 12, 1), true), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_PendingTask_UpdatesFields()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var newAssignee = Guid.NewGuid();
        var repository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        var task = new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending, IsRequired = true };
        repository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new UpdateEmployeeChecklistTaskCommandHandler(repository.Object, currentUser.Object)
            .Handle(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, newAssignee, new DateOnly(2026, 12, 15), false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.AssignedToId.Should().Be(newAssignee);
        task.IsRequired.Should().BeFalse();
    }
}
