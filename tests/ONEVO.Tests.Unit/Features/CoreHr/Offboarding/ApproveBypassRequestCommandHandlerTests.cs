using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class ApproveBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_NotTheAssignedApprover_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingTaskBypassRequest { Id = requestId, ApproverId = Guid.NewGuid(), Status = BypassRequestStatuses.Pending });

        var result = await new ApproveBypassRequestCommandHandler(bypassRepository.Object, Mock.Of<IEmployeeChecklistTaskRepository>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new ApproveBypassRequestCommand(requestId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ValidApproval_SetsTaskBypassedAndRequestApproved()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(approverId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var bypassRequest = new OffboardingTaskBypassRequest { Id = requestId, ApproverId = approverId, Status = BypassRequestStatuses.Pending, EmployeeChecklistTaskId = taskId };
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(bypassRequest);
        var task = new EmployeeChecklistTask { Id = taskId, Status = EmployeeChecklistTaskStatuses.InProgress };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new ApproveBypassRequestCommandHandler(bypassRepository.Object, taskRepository.Object, currentUser.Object, clock.Object)
            .Handle(new ApproveBypassRequestCommand(requestId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.Bypassed);
        bypassRequest.Status.Should().Be(BypassRequestStatuses.Approved);
    }
}
