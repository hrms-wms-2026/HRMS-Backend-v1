using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.ChangePassword;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Login;

public class ChangePasswordCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsForbidden_WhenCurrentPasswordDoesNotMatch()
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

        var auditLog = new Mock<IAuditLogRepository>();
        var outbox = new Mock<IOutboxWriter>();

        var handler = new ChangePasswordCommandHandler(
            users.Object, hasher.Object, auditLog.Object, outbox.Object,
            new Mock<IUnitOfWork>().Object, currentUser.Object, new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new ChangePasswordCommand("wrong-password", "NewPassword123"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        auditLog.Verify(a => a.AddAsync(It.IsAny<ONEVO.Domain.Features.Auth.Entities.AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(o => o.EnqueueAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Succeeds_HashesNewPassword_AndAudits()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        currentUser.SetupGet(c => c.TenantId).Returns(tenantId);
        currentUser.SetupGet(c => c.UserId).Returns(userId);

        var user = new User { Id = userId, PasswordHash = "old-hash" };
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify("current-password", "old-hash")).Returns(true);
        hasher.Setup(h => h.Hash("new-password")).Returns("new-hash");

        var auditLog = new Mock<IAuditLogRepository>();
        var outbox = new Mock<IOutboxWriter>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ChangePasswordCommandHandler(
            users.Object, hasher.Object, auditLog.Object, outbox.Object,
            unitOfWork.Object, currentUser.Object, new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new ChangePasswordCommand("current-password", "new-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
        auditLog.Verify(a => a.AddAsync(It.Is<ONEVO.Domain.Features.Auth.Entities.AuditLog>(l => l.Action == "user.password_changed"), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
