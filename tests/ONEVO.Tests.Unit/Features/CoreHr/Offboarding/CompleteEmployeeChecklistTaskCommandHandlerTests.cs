using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CompleteEmployeeChecklistTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_PendingBypassRequestExists_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending });
        bypassRepository.Setup(r => r.HasPendingForTaskAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new CompleteEmployeeChecklistTaskCommandHandler(taskRepository.Object, bypassRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CompleteEmployeeChecklistTaskCommand(employeeId, taskId), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_NoPendingBypass_MarksCompleted()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var task = new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        bypassRepository.Setup(r => r.HasPendingForTaskAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await new CompleteEmployeeChecklistTaskCommandHandler(taskRepository.Object, bypassRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CompleteEmployeeChecklistTaskCommand(employeeId, taskId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.Completed);
        task.CompletedAt.Should().NotBeNull();
    }
}
