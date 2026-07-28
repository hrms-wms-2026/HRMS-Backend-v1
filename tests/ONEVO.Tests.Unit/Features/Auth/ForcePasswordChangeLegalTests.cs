using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ForcePasswordChangeLegalTests
{
    [Fact]
    public async Task SuccessfulPasswordChange_DelegatesFinalIssuanceToLegalContinuation()
    {
        var tenantId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = "owner@example.com",
            PasswordHash = "old-hash",
            IsActive = true,
            MustChangePassword = true,
            PasswordSetByAdmin = true,
            TemporaryPasswordExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var users = new Mock<IUserRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var hasher = new Mock<IPasswordHasher>();
        var permissions = new Mock<IPermissionVersionService>();
        var continuation = new Mock<ILoginContinuationService>();
        var clock = new Mock<IDateTimeProvider>();
        var tenant = new Mock<ITenantContext>();

        tenant.SetupGet(t => t.IsResolved).Returns(true);
        tenant.SetupGet(t => t.ContextMode).Returns(TenantContextMode.Tenant);
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        users.Setup(u => u.GetByTenantAndEmailAsync(
                tenantId,
                user.Email,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        hasher.Setup(h => h.Verify("temporary", "old-hash")).Returns(true);
        hasher.Setup(h => h.Hash("NewPassword123!")).Returns("new-hash");
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var legalRequired = new LoginResponseDto(
            string.Empty,
            string.Empty,
            null,
            RequiresLegalAcceptance: true,
            LegalChallenge: "opaque",
            LegalCsrfToken: "csrf");
        continuation.Setup(c => c.FinishAuthenticatedLoginAsync(
                user,
                "force_password_change",
                "ip",
                "ua",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(legalRequired));

        var handler = new ForcePasswordChangeCommandHandler(
            users.Object,
            unitOfWork.Object,
            hasher.Object,
            permissions.Object,
            continuation.Object,
            clock.Object,
            tenant.Object);

        var result = await handler.Handle(
            new ForcePasswordChangeCommand(
                user.Email,
                "temporary",
                "NewPassword123!",
                "ip",
                "ua"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresLegalAcceptance.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();
        continuation.VerifyAll();
    }
}
