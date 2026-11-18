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
    private const string Sub = "google-sub-1";
    private const string Email = "user@example.com";

    private sealed class Fixture
    {
        public Mock<IGoogleIdTokenValidator> Google { get; } = new();
        public Mock<IPlatformOAuthAppResolver> OAuthApps { get; } = new();
        public Mock<IGoogleLoginCandidateRepository> Candidates { get; } = new();
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

        public void GoogleReturns(string email = Email, bool verified = true, string sub = Sub) =>
            Google
                .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GoogleIdTokenPayload(sub, email, verified, "Jane"));

        public void CandidatesAre(params GoogleLoginCandidateRow[] rows) =>
            Candidates
                .Setup(c => c.GetCandidatesAsync(Sub, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows);

        public void ContinuationReturns(Result<LoginResponseDto> result) =>
            Continuation
                .Setup(c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

        public BaseGoogleLoginCommandHandler Build() => new(
            Google.Object, OAuthApps.Object, Candidates.Object, WorkspaceChallenges.Object,
            Continuation.Object, Clock.Object);

        public Task<Result<BaseLoginResultDto>> Run() =>
            Build().Handle(new BaseGoogleLoginCommand("token", "ip", "ua"), default);
    }

    private static GoogleLoginCandidateRow Row(
        string slug = "acme", bool bySubject = false, string? domainsJson = null, Guid? tenantId = null, Guid? userId = null) =>
        new(tenantId ?? Guid.NewGuid(), userId ?? Guid.NewGuid(), slug, slug.ToUpperInvariant(), bySubject, domainsJson);

    private static LoginResponseDto Session(Guid userId, Guid tenantId) =>
        new("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8), new CurrentUserDto(userId, tenantId, Email));

    private static void ShouldBeGoogleAuthFailed(Result<BaseLoginResultDto> result)
    {
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.ErrorCode.Should().Be(GoogleLoginErrors.GoogleAuthFailedCode);
        result.Error.Should().Be("Google sign-in failed. Please try again.");
    }

    private static void ShouldBeNoAccess(Result<BaseLoginResultDto> result)
    {
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(GoogleLoginErrors.NoAccessCode);
        result.Error.Should().Be(
            "This Google account does not have access to Crownus. Ask your administrator for an invitation.");
    }

    [Fact]
    public async Task BadToken_Returns401GoogleAuthFailed_WithoutLookingUpCandidates()
    {
        var fixture = new Fixture();
        fixture.Google
            .Setup(g => g.ValidateAsync("token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleIdTokenPayload?)null);

        var result = await fixture.Run();

        ShouldBeGoogleAuthFailed(result);
        fixture.Candidates.Verify(
            c => c.GetCandidatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "workspace information must never be looked up before Google identity is verified");
    }

    [Fact]
    public async Task UnverifiedEmail_Returns401GoogleAuthFailed()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns(verified: false);

        ShouldBeGoogleAuthFailed(await fixture.Run());
    }

    [Fact]
    public async Task UnknownAccount_Returns403NoAccess()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre();

        ShouldBeNoAccess(await fixture.Run());
    }

    [Fact]
    public async Task LooksUpBySubjectAndNormalizedEmail()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns(email: "  User@Example.COM ");
        fixture.CandidatesAre();

        await fixture.Run();

        fixture.Candidates.Verify(c => c.GetCandidatesAsync(Sub, Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OverflowCandidates_Returns403NoAccess_NeverCreatesWorkspaceChallenge()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Enumerable.Range(0, 9).Select(i => Row($"tenant-{i}")).ToArray());

        ShouldBeNoAccess(await fixture.Run());
        fixture.WorkspaceChallenges.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InactiveUserOrTenant_ContinuationRejects_Returns403NoAccess()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row());
        fixture.ContinuationReturns(Result<LoginResponseDto>.Failure("whatever", 401));

        ShouldBeNoAccess(await fixture.Run());
    }

    [Fact]
    public async Task OneCandidateByEmail_ContinuesWithPendingLink_NotMatchedBySubject()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row(tenantId: tenantId, userId: userId));
        fixture.ContinuationReturns(Result<LoginResponseDto>.Success(Session(userId, tenantId)));

        var result = await fixture.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().NotBeNull();
        fixture.Continuation.Verify(c => c.ContinueAsync(
            It.Is<LoginContinuationRequest>(r =>
                r.TenantId == tenantId && r.UserId == userId && r.SwitchTenantContext
                && r.LegalChallengeOrigin == "google_sso"
                && r.ExternalIdentity != null
                && r.ExternalIdentity.Provider == "google"
                && r.ExternalIdentity.Subject == Sub
                && r.ExternalIdentity.Email == Email
                && !r.ExternalIdentity.MatchedBySubject),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubjectLink_LogsIn_EvenWhenGoogleEmailDiffersFromUserEmail()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        fixture.GoogleReturns(email: "someone.else@gmail.com");
        fixture.CandidatesAre(Row(bySubject: true, tenantId: tenantId, userId: userId));
        fixture.ContinuationReturns(Result<LoginResponseDto>.Success(Session(userId, tenantId)));

        var result = await fixture.Run();

        result.IsSuccess.Should().BeTrue();
        fixture.Continuation.Verify(c => c.ContinueAsync(
            It.Is<LoginContinuationRequest>(r => r.UserId == userId && r.ExternalIdentity!.MatchedBySubject),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisallowedEmailDomain_Returns403NoAccess()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row(domainsJson: """["acme.com"]"""));

        ShouldBeNoAccess(await fixture.Run());
        fixture.Continuation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnreadableDomainList_FailsClosed_Returns403NoAccess()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row(domainsJson: "not-json"));

        ShouldBeNoAccess(await fixture.Run());
    }

    [Theory]
    [InlineData("""["example.com"]""")]
    [InlineData("""["ACME.com","Example.COM"]""")]
    [InlineData("[]")]
    [InlineData(null)]
    public async Task AllowedOrUnsetDomainList_Continues(string? domainsJson)
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row(domainsJson: domainsJson, tenantId: tenantId, userId: userId));
        fixture.ContinuationReturns(Result<LoginResponseDto>.Success(Session(userId, tenantId)));

        (await fixture.Run()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DomainFilterLeavesOne_ContinuesWithThatCandidate()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        fixture.GoogleReturns();
        fixture.CandidatesAre(
            Row("blocked", domainsJson: """["acme.com"]"""),
            Row("open", tenantId: tenantId, userId: userId));
        fixture.ContinuationReturns(Result<LoginResponseDto>.Success(Session(userId, tenantId)));

        (await fixture.Run()).IsSuccess.Should().BeTrue();
        fixture.Continuation.Verify(c => c.ContinueAsync(
            It.Is<LoginContinuationRequest>(r => r.TenantId == tenantId), It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkspaceChallenges.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MultipleCandidates_CreatesWorkspaceChallengeCarryingThePendingLink()
    {
        var fixture = new Fixture();
        fixture.GoogleReturns();
        fixture.CandidatesAre(Row("acme"), Row("beta"));
        fixture.WorkspaceChallenges
            .Setup(w => w.CreateAsync(
                Email,
                "google_sso",
                It.Is<IReadOnlyList<WorkspaceCandidateSnapshot>>(l => l.Count == 2),
                It.Is<PendingExternalIdentity?>(p => p != null && p.Subject == Sub && p.Email == Email),
                "ip", "ua", TimeSpan.FromMinutes(5), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-google-challenge");

        var result = await fixture.Run();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().BeNull();
        result.Value.WorkspaceSelection!.LoginChallenge.Should().Be("raw-google-challenge");
        result.Value.WorkspaceSelection.Workspaces.Select(w => w.Slug).Should().BeEquivalentTo("acme", "beta");
    }

    [Fact]
    public void Handler_NeverDependsOnARepositoryThatCanCreateUsersOrIdentities()
    {
        // The handler only reads candidates and delegates to the continuation service. Linking the
        // Google identity happens in LoginContinuationService, only after login fully completes.
        var constructorParamTypes = typeof(BaseGoogleLoginCommandHandler).GetConstructors()[0].GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        constructorParamTypes.Should().NotContain(name =>
            name.Contains("UserExternalIdentity", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlatformUser", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("IUserRepository", StringComparison.OrdinalIgnoreCase));
    }
}
