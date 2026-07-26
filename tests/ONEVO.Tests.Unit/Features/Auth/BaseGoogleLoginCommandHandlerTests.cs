using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseGoogleLogin;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class BaseGoogleLoginCommandHandlerTests
{
    private sealed class Fixture
    {
        public Mock<IGoogleIdTokenValidator> Google { get; } = new();
        public Mock<IPlatformOAuthAppResolver> OAuthApps { get; } = new();
        public Mock<IBaseLoginCandidateRepository> Candidates { get; } = new();
        public Mock<ILoginWorkspaceSelectionChallengeRepository> WorkspaceChallenges { get; } = new();
        public Mock<ILoginContinuationService> Continuation { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();

        public Fixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-25T12:00:00Z"));
            OAuthApps
                .Setup(o => o.GetActiveAppForProviderAsync("google", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedPlatformOAuthApp("google", "client-id", "auth-url", "token-url", ["openid"]));
        }

        public BaseGoogleLoginCommandHandler Build() => new(
            Google.Object, OAuthApps.Object, Candidates.Object, WorkspaceChallenges.Object,
            Continuation.Object, Clock.Object);
    }

    [Fact]
    public async Task Handle_WrongAudience_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        fixture.Google
            .Setup(g => g.ValidateAsync("bad-token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleIdTokenPayload?)null);

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("bad-token", "ip", "ua"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.Candidates.Verify(
            c => c.GetCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "workspace information must never be looked up before Google identity is verified");
    }

    [Fact]
    public async Task Handle_UnverifiedEmail_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("sub-1", "user@example.com", false, "Jane"));

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsGenericFailure_WithoutDisclosingCandidateCount()
    {
        var fixture = new Fixture();
        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("sub-1", "user@example.com", true, "Jane"));
        fixture.Candidates
            .Setup(c => c.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BaseLoginCandidateRow>());

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_OverflowCandidates_ReturnsGenericFailure_NeverCreatesWorkspaceChallenge()
    {
        var fixture = new Fixture();
        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("sub-1", "user@example.com", true, "Jane"));
        var candidateRows = Enumerable.Range(0, 9)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        fixture.Candidates
            .Setup(c => c.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateRows);

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.WorkspaceChallenges.Verify(
            w => w.CreateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceCandidateSnapshot>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OneEligibleTenant_DelegatesToContinuation_WithGoogleSsoOrigin()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var candidateRow = new BaseLoginCandidateRow(tenantId, userId, "acme", "Acme Test", "irrelevant-hash");
        var sessionDto = new LoginResponseDto("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(userId, tenantId, "user@example.com"));

        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("sub-1", "user@example.com", true, "Jane"));
        fixture.Candidates
            .Setup(c => c.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateRow });
        fixture.Continuation
            .Setup(c => c.ContinueAsync(
                It.Is<LoginContinuationRequest>(r =>
                    r.TenantId == tenantId && r.UserId == userId && r.SwitchTenantContext
                    && r.LegalChallengeOrigin == "google_sso"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().Be(sessionDto);
        result.Value.WorkspaceSelection.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultipleEligibleTenants_ReturnsWorkspaceSelection_OnlyAfterGoogleVerified()
    {
        var fixture = new Fixture();
        var candidateRows = new[]
        {
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "hash-a"),
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "beta", "Beta Test", "hash-b")
        };
        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleIdTokenPayload("sub-1", "user@example.com", true, "Jane"));
        fixture.Candidates
            .Setup(c => c.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateRows);
        fixture.WorkspaceChallenges
            .Setup(w => w.CreateAsync(
                "user@example.com",
                "google_sso",
                It.Is<IReadOnlyList<WorkspaceCandidateSnapshot>>(l => l.Count == 2),
                "ip", "ua", TimeSpan.FromMinutes(5), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-google-challenge");

        var result = await fixture.Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().BeNull();
        result.Value.WorkspaceSelection.Should().NotBeNull();
        result.Value.WorkspaceSelection!.LoginChallenge.Should().Be("raw-google-challenge");
        result.Value.WorkspaceSelection.Workspaces.Should().HaveCount(2);
        result.Value.WorkspaceSelection.Workspaces.Should().OnlyContain(w => !string.IsNullOrEmpty(w.Slug) && !string.IsNullOrEmpty(w.DisplayName));
    }

    [Fact]
    public async Task Handle_NeverCreatesOrLinksAnyUserOrIdentity()
    {
        // BaseGoogleLoginCommandHandler must not depend on any repository capable of writing
        // users, user_external_identities, or platform_users - it can only read candidates and
        // delegate to the continuation service.
        var handlerType = typeof(BaseGoogleLoginCommandHandler);
        var constructorParamTypes = handlerType.GetConstructors()[0].GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        constructorParamTypes.Should().NotContain(name =>
            name.Contains("UserExternalIdentity", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlatformUser", StringComparison.OrdinalIgnoreCase) ||
            (name.Contains("IUserRepository", StringComparison.OrdinalIgnoreCase)));
    }
}
