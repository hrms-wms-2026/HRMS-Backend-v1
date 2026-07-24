using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.ApproveDeviceChange;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class ApproveDeviceChangeCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    [Fact]
    public async Task Handle_PendingRequest_RevokesCurrentAndActivatesCandidateAtomically()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var current = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DeviceId = "current-device",
            Status = "active"
        };
        var candidate = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DeviceId = "candidate-device",
            Status = "inactive"
        };
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            CurrentAgentId = current.Id,
            RequestedAgentId = candidate.Id,
            Status = "pending"
        };

        _repo.Setup(r => r.GetDeviceChangeRequestByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _repo.Setup(r => r.GetAgentByIdAsync(current.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _repo.Setup(r => r.GetAgentByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        _repo.Setup(r => r.EndActiveSessionAsync(
                current.DeviceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddSessionAsync(
                It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddOrUpdatePolicyAsync(
                It.IsAny<AgentPolicy>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ApproveDeviceChangeCommandHandler(_repo.Object, _uow.Object);
        var result = await handler.Handle(
            new ApproveDeviceChangeCommand(request.Id, "Laptop replaced", reviewerId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("revoked", current.Status);
        Assert.Equal("active", candidate.Status);
        Assert.Equal("approved", request.Status);
        Assert.Equal(reviewerId, request.ReviewedById);
        Assert.NotNull(request.ReviewedAt);
        Assert.Equal("Laptop replaced", request.ReviewComment);
        _repo.Verify(r => r.EndActiveSessionAsync(
            current.DeviceId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddSessionAsync(
            It.Is<AgentSession>(session =>
                session.DeviceId == candidate.DeviceId &&
                session.EmployeeId == employeeId &&
                session.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddOrUpdatePolicyAsync(
            It.Is<AgentPolicy>(policy => policy.AgentId == candidate.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonPendingRequest_ReturnsConflictWithoutSaving()
    {
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            Status = "rejected"
        };
        _repo.Setup(r => r.GetDeviceChangeRequestByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var handler = new ApproveDeviceChangeCommandHandler(_repo.Object, _uow.Object);
        var result = await handler.Handle(
            new ApproveDeviceChangeCommand(request.Id, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ChangedEmployeeBinding_ReturnsConflictWithoutSaving()
    {
        var employeeId = Guid.NewGuid();
        var current = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Status = "active"
        };
        var candidate = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Status = "inactive"
        };
        var request = new AgentDeviceChangeRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            CurrentAgentId = current.Id,
            RequestedAgentId = candidate.Id,
            Status = "pending"
        };
        _repo.Setup(r => r.GetDeviceChangeRequestByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _repo.Setup(r => r.GetAgentByIdAsync(current.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _repo.Setup(r => r.GetAgentByIdAsync(candidate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var handler = new ApproveDeviceChangeCommandHandler(_repo.Object, _uow.Object);
        var result = await handler.Handle(
            new ApproveDeviceChangeCommand(request.Id, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
