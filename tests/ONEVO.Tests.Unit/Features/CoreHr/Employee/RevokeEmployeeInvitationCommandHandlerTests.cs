using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class RevokeEmployeeInvitationCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IInvitationTokenRepository> _invitationTokenRepository = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IEmployeeInvitationAccess> _invitationAccess = new();

    public RevokeEmployeeInvitationCommandHandlerTests()
    {
        _employeeRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid id, CancellationToken _) => new EmployeeEntity { Id = id, TenantId = tenantId });
        _invitationAccess
            .Setup(a => a.CanManageAsync(It.IsAny<InvitationToken>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RevokeEmployeeInvitationCommandHandler CreateHandler() => new(
        _employeeRepository.Object,
        _invitationTokenRepository.Object,
        _positionAssignmentRepository.Object,
        _unitOfWork.Object,
        _currentUser.Object,
        _clock.Object,
        _invitationAccess.Object);

    private static InvitationToken BuildPendingInvitation(Guid tenantId, Guid employeeId, Guid? positionAssignmentId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        PositionAssignmentId = positionAssignmentId,
        InvitedEmail = "person@example.com",
        InvitedFullName = "Person Example",
        Status = "pending",
        TokenHash = "hash",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_PendingInvitation_RevokesTokenAndCancelsReservedSeat()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionAssignmentId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, positionAssignmentId);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(invitation.RevokedAt);
        _positionAssignmentRepository.Verify(
            r => r.CancelPlannedAsync(tenantId, positionAssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyAcceptedInvitation_ReturnsFailure_DoesNotRevoke()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, Guid.NewGuid());
        invitation.UsedAt = DateTimeOffset.UtcNow.AddHours(-1);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("This invitation has already been accepted.", result.Error);
        _positionAssignmentRepository.Verify(
            r => r.CancelPlannedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyRevokedInvitation_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, Guid.NewGuid());
        invitation.RevokedAt = DateTimeOffset.UtcNow.AddHours(-1);

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("This invitation has already been revoked.", result.Error);
    }

    [Fact]
    public async Task Handle_NoInvitationFound_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("No invitation has been sent to this employee.", result.Error);
    }

    [Fact]
    public async Task Handle_CallerIsNotInviterNorUserManager_ReturnsForbidden_DoesNotRevoke()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var invitation = BuildPendingInvitation(tenantId, employeeId, Guid.NewGuid());

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _invitationTokenRepository
            .Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);
        _invitationAccess
            .Setup(a => a.CanManageAsync(invitation, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new RevokeEmployeeInvitationCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(invitation.RevokedAt);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
