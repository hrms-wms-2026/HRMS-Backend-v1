using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.Queries.GetInvitationByToken;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Invite;

public class GetInvitationByTokenQueryHandlerTests
{
    private const string RawToken = "test-raw-token-xyz789";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T12:00:00Z");

    private readonly Mock<IInvitationTokenRepository> _invitations = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<ITenantAuthPolicyRepository> _policies = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public GetInvitationByTokenQueryHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _policies.Setup(p => p.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAuthPolicy?)null);
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false, PasswordHash = string.Empty });
    }

    private static InvitationToken MakeInvitation(
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? usedAt = null,
        DateTimeOffset? revokedAt = null,
        Guid? roleId = null,
        string? completionMethodsJson = null,
        bool? allowGoogleEmailMismatch = null,
        string? allowedEmailDomainsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        UserId = UserId,
        RoleId = roleId,
        InvitedEmail = "invited@example.com",
        InvitedFullName = "Jane Doe",
        TokenHash = InvitationTokenHasher.Hash(RawToken),
        ExpiresAt = expiresAt ?? Now.AddDays(1),
        UsedAt = usedAt,
        RevokedAt = revokedAt,
        CompletionMethodsJson = completionMethodsJson,
        AllowGoogleEmailMismatch = allowGoogleEmailMismatch,
        AllowedEmailDomainsJson = allowedEmailDomainsJson
    };

    private static Tenant MakeTenant() => new() { Id = TenantId, Name = "Acme Corp", Slug = "acme" };

    private GetInvitationByTokenQueryHandler BuildSut() => new(
        _invitations.Object,
        _tenants.Object,
        _roles.Object,
        _policies.Object,
        _users.Object,
        _clock.Object);

    [Fact]
    public async Task Handle_TokenNotFound_ReturnsNotFound()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvitationToken?)null);

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TenantMissing_ReturnsNotFound()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_PendingInvitation_NoRoleAssigned_ReturnsDto_WithEmptyRoleName_AndPendingStatus()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantName.Should().Be("Acme Corp");
        result.Value.InvitedEmail.Should().Be("invited@example.com");
        result.Value.FirstName.Should().Be("Jane");
        result.Value.LastName.Should().Be("Doe");
        result.Value.RoleName.Should().BeEmpty();
        result.Value.Status.Should().Be("pending");
        _roles.Verify(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvitationWithRole_ReturnsDto_WithRoleName()
    {
        var roleId = Guid.NewGuid();
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(roleId: roleId));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _roles.Setup(r => r.GetByIdForTenantAsync(TenantId, roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role { Id = roleId, TenantId = TenantId, Name = "HR Manager" });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RoleName.Should().Be("HR Manager");
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ReturnsDto_WithExpiredStatus()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(expiresAt: Now.AddDays(-1)));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("expired");
    }

    [Fact]
    public async Task Handle_UsedInvitation_ReturnsDto_WithAcceptedStatus()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(usedAt: Now.AddHours(-1)));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("accepted");
    }

    [Fact]
    public async Task Handle_RevokedInvitation_ReturnsDto_WithRevokedStatus()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(revokedAt: Now.AddHours(-1)));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("revoked");
    }

    [Fact]
    public async Task Handle_NoCompletionMethodsRestriction_BothPasswordAndGoogleEnabled()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(completionMethodsJson: null));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PasswordSetupEnabled.Should().BeTrue();
        result.Value.GoogleSignInEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CompletionMethodsRestrictedToGoogle_PasswordDisabled()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(completionMethodsJson: JsonSerializer.Serialize(new[] { "google" })));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PasswordSetupEnabled.Should().BeFalse();
        result.Value.GoogleSignInEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CompletionMethodsRestrictedToPassword_GoogleDisabled()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(completionMethodsJson: JsonSerializer.Serialize(new[] { "password" })));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PasswordSetupEnabled.Should().BeTrue();
        result.Value.GoogleSignInEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AllowGoogleEmailMismatchOnInvitation_TakesPrecedenceOverPolicyDefault()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(allowGoogleEmailMismatch: true));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAuthPolicy { TenantId = TenantId, GoogleEmailMismatchDefault = false });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllowGoogleEmailMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllowGoogleEmailMismatchNotSetOnInvitation_FallsBackToPolicyDefault()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(allowGoogleEmailMismatch: null));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAuthPolicy { TenantId = TenantId, GoogleEmailMismatchDefault = true });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllowGoogleEmailMismatch.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllowedEmailDomains_MergesInvitationAndPolicyDomains_Deduplicated()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(
                allowedEmailDomainsJson: JsonSerializer.Serialize(new[] { "acme.com", "Shared.com" })));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _policies.Setup(p => p.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAuthPolicy
            {
                TenantId = TenantId,
                AllowedLoginDomainsJson = JsonSerializer.Serialize(new[] { "shared.com", "beta.com" })
            });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllowedEmailDomains.Should().BeEquivalentTo(new[] { "acme.com", "shared.com", "beta.com" });
    }

    [Fact]
    public async Task Handle_MalformedAllowedEmailDomainsJson_IsIgnored_ReturnsEmptyDomains()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation(allowedEmailDomainsJson: "{not-valid-json"));
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AllowedEmailDomains.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturningUserWithExistingCredentials_SetsRequiresPasswordFalse()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = true, PasswordHash = "existing-hash" });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_BrandNewUser_SetsRequiresPasswordTrue()
    {
        _invitations.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeInvitation());
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeTenant());
        _users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, IsActive = false, PasswordHash = string.Empty });

        var result = await BuildSut().Handle(new GetInvitationByTokenQuery(RawToken), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresPassword.Should().BeTrue();
    }
}
