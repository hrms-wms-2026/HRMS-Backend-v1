using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class RejectBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidRejection_RestoresTaskToPriorStatus()
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
        var bypassRequest = new OffboardingTaskBypassRequest
        {
            Id = requestId, ApproverId = approverId, Status = BypassRequestStatuses.Pending,
            EmployeeChecklistTaskId = taskId, PriorTaskStatus = EmployeeChecklistTaskStatuses.InProgress,
        };
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(bypassRequest);
        var task = new EmployeeChecklistTask { Id = taskId, Status = EmployeeChecklistTaskStatuses.InProgress };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new RejectBypassRequestCommandHandler(bypassRepository.Object, taskRepository.Object, currentUser.Object, clock.Object)
            .Handle(new RejectBypassRequestCommand(requestId, "Not approved."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.InProgress);
        bypassRequest.Status.Should().Be(BypassRequestStatuses.Rejected);
        bypassRequest.DecisionComment.Should().Be("Not approved.");
    }
}
