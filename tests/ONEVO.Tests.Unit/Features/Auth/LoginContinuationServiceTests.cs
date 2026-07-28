using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class LoginContinuationServiceTests
{
    private sealed class Fixture
    {
        public Mock<ITenantRepository> Tenants { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IUserMfaRepository> UserMfas { get; } = new();
        public Mock<IMfaChallengeStore> MfaChallenges { get; } = new();
        public Mock<ITenantContextSwitcher> TenantSwitcher { get; } = new();
        public Mock<ILegalAcceptanceChecker> LegalChecker { get; } = new();
        public Mock<ILegalLoginChallengeRepository> LegalChallenges { get; } = new();
        public Mock<ILoginSessionMaterialFactory> SessionMaterialFactory { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();

        public Fixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-25T12:00:00Z"));
            LegalChecker
                .Setup(c => c.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LegalAcceptanceCheckResult(LegalAcceptanceStatus.Complete, true, []));
        }

        public LoginContinuationService Build() => new(
            Tenants.Object, Users.Object, UserMfas.Object, MfaChallenges.Object, TenantSwitcher.Object,
            LegalChecker.Object, LegalChallenges.Object, SessionMaterialFactory.Object, UnitOfWork.Object, Clock.Object);
    }

    private static Tenant ActiveTenant(Guid id) => new()
    {
        Id = id, Name = "Acme", Slug = "acme", CompanySizeRange = "51-200", Status = TenantStatus.Active
    };

    private static User ActiveUser(Guid tenantId, Guid userId) => new()
    {
        Id = userId, TenantId = tenantId, Email = "user@example.com", PasswordHash = "irrelevant",
        FirstName = "Jane", LastName = "Doe", IsActive = true
    };

    [Fact]
    public async Task ContinueAsync_TenantNotFound_ReturnsGenericFailure_AndNeverSwitches()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var request = new LoginContinuationRequest(tenantId, Guid.NewGuid(), true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("generic-failure");
        result.StatusCode.Should().Be(401);
        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinueAsync_TenantSuspended_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        tenant.Status = TenantStatus.Suspended;
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var request = new LoginContinuationRequest(tenant.Id, Guid.NewGuid(), true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ContinueAsync_WhenSwitchTenantContextIsFalse_NeverCallsSwitcher()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        fixture.SessionMaterialFactory
            .Setup(f => f.PrepareAsync(user, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto("c", "h", null)));

        var request = new LoginContinuationRequest(tenant.Id, user.Id, false, "generic-failure", "password", null, null);
        await fixture.Build().ContinueAsync(request);

        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinueAsync_UserNotActive_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        user.IsActive = false;
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("generic-failure");
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ContinueAsync_MustChangePassword_ReturnsPasswordChangeRequired_WithoutCheckingMfaOrLegal()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        user.MustChangePassword = true;
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresPasswordChange.Should().BeTrue();
        fixture.UserMfas.Verify(
            m => m.GetTotpAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.LegalChecker.Verify(
            c => c.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinueAsync_ExpiredAdminSetTemporaryPassword_ReturnsSpecificFailure()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        user.MustChangePassword = true;
        user.PasswordSetByAdmin = true;
        user.TemporaryPasswordExpiresAt = DateTimeOffset.Parse("2026-07-25T11:00:00Z");
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Temporary password has expired. Request a new one from your administrator.");
    }

    [Fact]
    public async Task ContinueAsync_VerifiedMfaExists_ReturnsMfaRequired_WithoutCheckingLegalOrCreatingSession()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        var mfa = new UserMfa { Id = Guid.NewGuid(), UserId = user.Id, MethodType = "totp", IsVerified = true };
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(mfa);
        fixture.MfaChallenges
            .Setup(m => m.CreateAsync(
                user.Id,
                user.TenantId,
                "password",
                TimeSpan.FromMinutes(10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-mfa-challenge");

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeTrue();
        result.Value.MfaChallenge.Should().Be("raw-mfa-challenge");
        fixture.LegalChecker.Verify(
            c => c.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.SessionMaterialFactory.Verify(
            f => f.PrepareAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ContinueAsync_OnlyUnverifiedTotpSetupExists_DoesNotRequireMfa_SoLoginNeverCompletesFirstTimeSetup()
    {
        // LoginContinuationService only ever checks GetTotpAsync(userId, isVerified: true, ...) to decide
        // whether to create an onevo_mfa challenge. A pending (unverified) TOTP setup from mfa/enable is
        // invisible here, so no challenge is issued and mfa/verify is never reached for first-time setup -
        // confirming setup is only possible through mfa/confirm-setup.
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        var pending = new UserMfa { Id = Guid.NewGuid(), UserId = user.Id, MethodType = "totp", IsVerified = false };
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(pending);
        fixture.SessionMaterialFactory
            .Setup(f => f.PrepareAsync(user, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(new LoginResponseDto("c", "h", null)));

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresMfa.Should().BeFalse();
        pending.IsVerified.Should().BeFalse();
        fixture.MfaChallenges.Verify(
            m => m.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.UserMfas.Verify(
            m => m.GetTotpAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Never,
            "login must never consult a pending setup when deciding whether to issue an MFA challenge");
    }

    [Fact]
    public async Task ContinueAsync_LegalAcceptancePending_ReturnsLegalAcceptanceRequired_WithoutCreatingSessionOrUpdatingLastLoginAt()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        var pendingDocs = new[] { new PendingLegalDocumentDto("terms", "v2", "Terms", null, null, "/api/v1/legal/documents/terms/v2", "hash") };
        fixture.LegalChecker
            .Setup(c => c.CheckAsync(tenant.Id, user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(LegalAcceptanceStatus.Pending, false, pendingDocs));
        fixture.LegalChallenges
            .Setup(l => l.CreateAsync(tenant.Id, user.Id, "password", TimeSpan.FromMinutes(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("raw-legal-challenge", "raw-legal-csrf"));

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", null, null);
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresLegalAcceptance.Should().BeTrue();
        result.Value.LegalChallenge.Should().Be("raw-legal-challenge");
        result.Value.LegalCsrfToken.Should().Be("raw-legal-csrf");
        result.Value.PendingLegalDocuments.Should().BeEquivalentTo(pendingDocs);
        fixture.SessionMaterialFactory.Verify(
            f => f.PrepareAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "LastLoginAt must be updated only when the final session is actually issued");
    }

    [Fact]
    public async Task ContinueAsync_LegalAcceptanceComplete_IssuesSession_AndUpdatesLastLoginAtOnce()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        var sessionDto = new LoginResponseDto("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(user.Id, user.TenantId, user.Email));
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.UserMfas.Setup(m => m.GetTotpAsync(user.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync((UserMfa?)null);
        fixture.SessionMaterialFactory
            .Setup(f => f.PrepareAsync(user, "127.0.0.1", "test-agent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var request = new LoginContinuationRequest(tenant.Id, user.Id, true, "generic-failure", "password", "127.0.0.1", "test-agent");
        var result = await fixture.Build().ContinueAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sessionDto);
        user.LastLoginAt.Should().Be(fixture.Clock.Object.UtcNow);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == tenant.Id && e.Slug == tenant.Slug),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinishAuthenticatedLoginAsync_SkipsTenantAndUserResolution_AndRunsLegalThenSession()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        var sessionDto = new LoginResponseDto("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8));
        fixture.SessionMaterialFactory
            .Setup(f => f.PrepareAsync(user, "ip", "ua", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var result = await fixture.Build().FinishAuthenticatedLoginAsync(user, "mfa", "ip", "ua");

        result.IsSuccess.Should().BeTrue();
        fixture.Tenants.Verify(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Users.Verify(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.LegalChecker.Verify(c => c.CheckAsync(user.TenantId, user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
