using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CreateBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_ApproverIsRequester_ReturnsUnprocessableEntity()
    {
        var currentUser = new Mock<ICurrentUser>();
        var actingUserId = Guid.NewGuid();
        currentUser.Setup(c => c.UserId).Returns(actingUserId);

        var result = await new CreateBypassRequestCommandHandler(
                Mock.Of<IEmployeeChecklistTaskRepository>(), Mock.Of<IOffboardingTaskBypassRequestRepository>(),
                Mock.Of<IOffboardingRecordRepository>(), Mock.Of<IUnitOfWork>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new CreateBypassRequestCommand(Guid.NewGuid(), Guid.NewGuid(), actingUserId, "reason", null), CancellationToken.None);

        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_TaskNotBypassable_ReturnsUnprocessableEntity()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, IsBypassable = false });

        var result = await new CreateBypassRequestCommandHandler(
                taskRepository.Object, Mock.Of<IOffboardingTaskBypassRequestRepository>(),
                Mock.Of<IOffboardingRecordRepository>(), Mock.Of<IUnitOfWork>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new CreateBypassRequestCommand(employeeId, taskId, Guid.NewGuid(), "reason", null), CancellationToken.None);

        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesRequestWithPriorTaskStatusSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, IsBypassable = true, Status = EmployeeChecklistTaskStatuses.InProgress, BypassPenaltyDescription = "Default penalty" });
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = recordId });

        OffboardingTaskBypassRequest? added = null;
        bypassRepository.Setup(r => r.AddAsync(It.IsAny<OffboardingTaskBypassRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OffboardingTaskBypassRequest, CancellationToken>((r, _) => added = r).Returns(Task.CompletedTask);

        var result = await new CreateBypassRequestCommandHandler(
                taskRepository.Object, bypassRepository.Object, offboardingRecordRepository.Object,
                unitOfWork.Object, currentUser.Object, clock.Object)
            .Handle(new CreateBypassRequestCommand(employeeId, taskId, Guid.NewGuid(), "Payment processed in advance.", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.PriorTaskStatus.Should().Be(EmployeeChecklistTaskStatuses.InProgress);
        added.PenaltyDescription.Should().Be("Default penalty");
        added.OffboardingRecordId.Should().Be(recordId);
    }
}
