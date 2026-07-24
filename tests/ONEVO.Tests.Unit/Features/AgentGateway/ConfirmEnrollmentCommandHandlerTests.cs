using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class ConfirmEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    [Fact]
    public async Task Handle_AuthenticatedEmployee_StoresEmployeeIdNotUserId()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        _tenant.SetupGet(x => x.IsResolved).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(userId);
        _profiles
            .Setup(x => x.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = employeeId,
                UserId = userId,
                TenantId = tenantId
            });
        _repo
            .Setup(x => x.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingChallenge(enrollmentId));
        _repo
            .Setup(x => x.TryMarkChallengeConfirmedAsync(
                enrollmentId,
                It.IsAny<string>(),
                tenantId,
                employeeId,
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            new ConfirmEnrollmentCommand(enrollmentId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(x => x.TryMarkChallengeConfirmedAsync(
            enrollmentId,
            It.IsAny<string>(),
            tenantId,
            employeeId,
            userId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithoutEmployee_ReturnsForbiddenWithoutConfirming()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        _tenant.SetupGet(x => x.IsResolved).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(userId);
        _profiles
            .Setup(x => x.GetEmployeeByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        _repo
            .Setup(x => x.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingChallenge(enrollmentId));

        var result = await CreateHandler().Handle(
            new ConfirmEnrollmentCommand(enrollmentId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _repo.Verify(x => x.TryMarkChallengeConfirmedAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private ConfirmEnrollmentCommandHandler CreateHandler() =>
        new(
            _repo.Object,
            _tenant.Object,
            _currentUser.Object,
            _profiles.Object,
            _unitOfWork.Object);

    private static AgentEnrollmentChallenge PendingChallenge(Guid id) =>
        new()
        {
            Id = id,
            DeviceId = "device-01",
            DeviceName = "DESKTOP-01",
            OsVersion = "Windows 11",
            AgentVersion = "1.0.0",
            Status = "pending",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
}
