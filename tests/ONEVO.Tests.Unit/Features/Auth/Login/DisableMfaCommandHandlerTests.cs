using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.MfaDisable;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Login;

public class DisableMfaCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCurrentPasswordIsWrong()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = "stored-hash" });

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("wrong-password", "stored-hash")).Returns(false);

        var userMfa = new Mock<IUserMfaRepository>();

        var handler = new DisableMfaCommandHandler(
            users.Object, hasher.Object, userMfa.Object, new Mock<IAuditLogRepository>().Object,
            new Mock<IOutboxWriter>().Object, new Mock<IUnitOfWork>().Object, currentUser.Object,
            new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new DisableMfaCommand("wrong-password"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        userMfa.Verify(m => m.Remove(It.IsAny<UserMfa>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RemovesMfaRegistrations_WhenPasswordCorrect()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = userId, PasswordHash = "stored-hash" });

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("correct-password", "stored-hash")).Returns(true);

        var verifiedMfa = new UserMfa { Id = Guid.NewGuid(), UserId = userId, MethodType = "totp", IsVerified = true };
        var userMfa = new Mock<IUserMfaRepository>();
        userMfa.Setup(m => m.GetTotpAsync(userId, true, It.IsAny<CancellationToken>())).ReturnsAsync(verifiedMfa);
        userMfa.Setup(m => m.GetTotpAsync(userId, false, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);

        var auditLog = new Mock<IAuditLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DisableMfaCommandHandler(
            users.Object, hasher.Object, userMfa.Object, auditLog.Object,
            new Mock<IOutboxWriter>().Object, unitOfWork.Object, currentUser.Object,
            new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new DisableMfaCommand("correct-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        userMfa.Verify(m => m.Remove(verifiedMfa), Times.Once);
        auditLog.Verify(a => a.AddAsync(It.Is<AuditLog>(l => l.Action == "user.mfa_disabled"), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
