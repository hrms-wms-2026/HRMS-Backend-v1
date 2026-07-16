using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.Login;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class LoginTenantScopeTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserMfaRepository> _userMfas = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMfaChallengeStore> _mfaChallenges = new();
    private readonly Mock<ILoginSessionMaterialFactory> _issuer = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();

    private Mock<ITenantContext> TenantCtx(
        TenantContextMode mode = TenantContextMode.Tenant,
        bool resolved = true,
        TenantStatus status = TenantStatus.Active)
    {
        var m = new Mock<ITenantContext>();
        m.Setup(c => c.ContextMode).Returns(mode);
        m.Setup(c => c.IsResolved).Returns(resolved);
        m.Setup(c => c.TenantId).Returns(_tenantId);
        m.Setup(c => c.Status).Returns(status);
        return m;
    }

    private LoginCommandHandler BuildHandler(ITenantContext ctx) =>
        new(_users.Object, _userMfas.Object, _uow.Object, _hasher.Object, _mfaChallenges.Object, _issuer.Object, ctx);

    [Fact]
    public async Task Login_UsesTenantScopedLookup_NotGlobalEmail()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Email = "a@b.com",
            IsActive = true
        };

        _users.Setup(u => u.GetByTenantAndEmailAsync(_tenantId, "a@b.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _userMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((UserMfa?)null);
        _issuer.Setup(i => i.PrepareAsync(user, null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto(
                   CsrfTokenHash: "sess",
                   CsrfToken: "csrf",
                   ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
                   User: new CurrentUserDto(user.Id, _tenantId, user.Email))));

        var handler = BuildHandler(TenantCtx().Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);

        result.IsSuccess.Should().BeTrue();
        _users.Verify(u => u.GetByNormalizedEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.GetByTenantAndEmailAsync(_tenantId, "a@b.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_WhenContextNotResolved_ReturnsFailure()
    {
        var handler = BuildHandler(TenantCtx(resolved: false).Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Tenant context is not resolved.");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Login_WhenContextModeIsAdmin_ReturnsFailure()
    {
        var handler = BuildHandler(TenantCtx(mode: TenantContextMode.Admin).Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Tenant context is not resolved.");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Login_WhenContextModeIsSystem_ReturnsFailure()
    {
        var handler = BuildHandler(TenantCtx(mode: TenantContextMode.System).Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenTenantSuspended_ReturnsFailure()
    {
        var handler = BuildHandler(TenantCtx(status: TenantStatus.Suspended).Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenTenantCancelled_ReturnsFailure()
    {
        var handler = BuildHandler(TenantCtx(status: TenantStatus.Cancelled).Object);
        var result = await handler.Handle(new LoginCommand("a@b.com", "pass", null, null), default);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenTenantTrial_Succeeds()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Email = "trial@b.com",
            IsActive = true
        };

        _users.Setup(u => u.GetByTenantAndEmailAsync(_tenantId, "trial@b.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _userMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((UserMfa?)null);
        _issuer.Setup(i => i.PrepareAsync(user, null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto(
                   CsrfTokenHash: "sess",
                   CsrfToken: "csrf",
                   ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
                   User: new CurrentUserDto(user.Id, _tenantId, user.Email))));

        var handler = BuildHandler(TenantCtx(status: TenantStatus.Trial).Object);
        var result = await handler.Handle(new LoginCommand("trial@b.com", "pass", null, null), default);

        result.IsSuccess.Should().BeTrue();
    }
}
