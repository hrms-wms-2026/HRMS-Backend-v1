using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.Login;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class CrossTenantLeakageTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ILoginContinuationService> _continuation = new();

    private LoginCommandHandler BuildHandler(Guid tenantId, TenantStatus status = TenantStatus.Active)
    {
        var ctx = new Mock<ITenantContext>();
        ctx.Setup(c => c.IsResolved).Returns(true);
        ctx.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);
        ctx.Setup(c => c.TenantId).Returns(tenantId);
        ctx.Setup(c => c.Status).Returns(status);
        return new LoginCommandHandler(_users.Object, _hasher.Object, _continuation.Object, ctx.Object);
    }

    private void SetupContinuationSuccess(Guid tenantId, Guid userId, string email)
    {
        _continuation
            .Setup(c => c.ContinueAsync(
                It.Is<LoginContinuationRequest>(r => r.TenantId == tenantId && r.UserId == userId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto(
                "sess", "csrf", DateTimeOffset.UtcNow.AddMinutes(30),
                new CurrentUserDto(userId, tenantId, email))));
    }

    [Fact]
    public async Task SameEmail_TwoTenants_LoginResolvesCorrectTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = new User { Id = Guid.NewGuid(), TenantId = tenantA, Email = "shared@example.com", IsActive = true };
        var userB = new User { Id = Guid.NewGuid(), TenantId = tenantB, Email = "shared@example.com", IsActive = true };

        _users.Setup(u => u.GetByTenantAndEmailAsync(tenantA, "shared@example.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(userA);
        _users.Setup(u => u.GetByTenantAndEmailAsync(tenantB, "shared@example.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(userB);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        SetupContinuationSuccess(tenantA, userA.Id, "shared@example.com");
        SetupContinuationSuccess(tenantB, userB.Id, "shared@example.com");

        var resultA = await BuildHandler(tenantA).Handle(new LoginCommand("shared@example.com", "pass", null, null), default);
        var resultB = await BuildHandler(tenantB).Handle(new LoginCommand("shared@example.com", "pass", null, null), default);

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();
        // Global lookup must never be used
        _users.Verify(u => u.GetByNormalizedEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuspendedTenant_Login_ReturnsFailure()
    {
        var tenantId = Guid.NewGuid();
        var result = await BuildHandler(tenantId, TenantStatus.Suspended)
            .Handle(new LoginCommand("a@b.com", "pass", null, null), default);

        result.IsSuccess.Should().BeFalse();
        _users.Verify(u => u.GetByTenantAndEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvisioningTenant_Login_ReturnsFailure()
    {
        var result = await BuildHandler(Guid.NewGuid(), TenantStatus.Provisioning)
            .Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CancelledTenant_Login_ReturnsFailure()
    {
        var result = await BuildHandler(Guid.NewGuid(), TenantStatus.Cancelled)
            .Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task TrialTenant_Login_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), TenantId = tenantId, Email = "a@b.com", IsActive = true };
        _users.Setup(u => u.GetByTenantAndEmailAsync(tenantId, "a@b.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        SetupContinuationSuccess(tenantId, user.Id, "a@b.com");

        var result = await BuildHandler(tenantId, TenantStatus.Trial)
            .Handle(new LoginCommand("a@b.com", "pass", null, null), default);

        result.IsSuccess.Should().BeTrue();
    }
}
