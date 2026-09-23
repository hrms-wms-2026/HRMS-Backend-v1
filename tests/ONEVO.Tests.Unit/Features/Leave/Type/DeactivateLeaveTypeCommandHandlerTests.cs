using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class DeactivateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();

    public DeactivateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_PendingRequestsExist_NotConfirmed_ReturnsConflictWithCount()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("3", result.Error);
        _repoMock.Verify(r => r.Update(It.IsAny<LeaveType>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingRequestsExist_Confirmed_Deactivates()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entity.IsActive);
        _repoMock.Verify(r => r.Update(entity), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPendingRequests_DeactivatesWithoutConfirmation()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entity.IsActive);
    }
}
