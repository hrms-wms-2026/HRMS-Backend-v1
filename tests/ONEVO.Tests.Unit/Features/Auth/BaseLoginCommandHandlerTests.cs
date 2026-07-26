using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseLogin;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class BaseLoginCommandHandlerTests
{
    private sealed class HandlerFixture
    {
        public Mock<IBaseLoginCandidateRepository> Candidates { get; } = new();
        public Mock<IBaseLoginFixedWorkVerifier> Verifier { get; } = new();
        public Mock<ILoginWorkspaceSelectionChallengeRepository> WorkspaceChallenges { get; } = new();
        public Mock<ILoginContinuationService> Continuation { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-24T12:00:00Z"));
        }

        public BaseLoginCommandHandler Build() => new(
            Candidates.Object,
            Verifier.Object,
            WorkspaceChallenges.Object,
            Continuation.Object,
            Clock.Object);
    }

    [Fact]
    public async Task Handle_WithZeroCandidates_ReturnsGenericInvalidCredentials()
    {
        var fixture = new HandlerFixture();
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BaseLoginCandidateRow>());
        fixture.Verifier
            .Setup(v => v.VerifyAsync(
                It.IsAny<IReadOnlyList<BaseLoginCandidateRow>>(),
                "SubmittedPassword1!",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome([], false));

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "SubmittedPassword1!", "127.0.0.1", "test-agent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
        result.StatusCode.Should().Be(401, "the task requires HTTP 401 for zero-match/wrong-password/overflow/disabled/inactive/locked, not Result<T>.Failure's 400 default");
        fixture.Continuation.Verify(
            c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ReturnsGenericInvalidCredentials()
    {
        var fixture = new HandlerFixture();
        var candidateRow = new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "real-hash");
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateRow });
        fixture.Verifier
            .Setup(v => v.VerifyAsync(new[] { candidateRow }, "WrongPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome([], false));

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "WrongPassword1!", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.Continuation.Verify(
            c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithOverflow_ReturnsGenericInvalidCredentials_AndNeverCreatesAWorkspaceChallenge()
    {
        var fixture = new HandlerFixture();
        var candidateRows = Enumerable.Range(0, 9)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateRows);
        fixture.Verifier
            .Setup(v => v.VerifyAsync(candidateRows, "SubmittedPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome([], true));

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "SubmittedPassword1!", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.WorkspaceChallenges.Verify(
            w => w.CreateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceCandidateSnapshot>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithOneMatch_DelegatesToContinuationService_WithTenantSwitchRequested()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var candidateRow = new BaseLoginCandidateRow(tenantId, userId, "acme", "Acme Test", "real-hash");
        var sessionDto = new LoginResponseDto("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(userId, tenantId, "user@example.com"));

        fixture.Candidates.Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateRow });
        fixture.Verifier.Setup(v => v.VerifyAsync(new[] { candidateRow }, "SubmittedPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome(new[] { candidateRow }, false));
        fixture.Continuation
            .Setup(c => c.ContinueAsync(
                It.Is<LoginContinuationRequest>(r =>
                    r.TenantId == tenantId && r.UserId == userId && r.SwitchTenantContext
                    && r.GenericFailureMessage == "Invalid email or password." && r.LegalChallengeOrigin == "password"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "SubmittedPassword1!", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().Be(sessionDto);
        result.Value.WorkspaceSelection.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithOneMatch_AndContinuationFails_PropagatesErrorAndStatusCode()
    {
        var fixture = new HandlerFixture();
        var candidateRow = new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "real-hash");

        fixture.Candidates.Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateRow });
        fixture.Verifier.Setup(v => v.VerifyAsync(new[] { candidateRow }, "SubmittedPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome(new[] { candidateRow }, false));
        fixture.Continuation
            .Setup(c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Failure("Invalid email or password.", 401));

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "SubmittedPassword1!", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Handle_WithMultipleMatches_ReturnsWorkspaceSelection_AndCreatesTheChallengeWithAllMatchedSnapshots()
    {
        var fixture = new HandlerFixture();
        var candidateRows = new[]
        {
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "hash-a"),
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "beta", "Beta Test", "hash-b"),
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "gamma", "Gamma Test", "hash-c")
        };
        fixture.Candidates.Setup(r => r.GetCandidatesAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateRows);
        fixture.Verifier.Setup(v => v.VerifyAsync(candidateRows, "SubmittedPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseLoginVerificationOutcome(candidateRows, false));
        fixture.WorkspaceChallenges
            .Setup(w => w.CreateAsync(
                "user@example.com",
                "password",
                It.Is<IReadOnlyList<WorkspaceCandidateSnapshot>>(list => list.Count == 3),
                "127.0.0.1", "test-agent", TimeSpan.FromMinutes(5), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-login-challenge");

        var result = await fixture.Build().Handle(
            new BaseLoginCommand("user@example.com", "SubmittedPassword1!", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Session.Should().BeNull();
        result.Value.WorkspaceSelection.Should().NotBeNull();
        result.Value.WorkspaceSelection!.LoginChallenge.Should().Be("raw-login-challenge");
        result.Value.WorkspaceSelection.Workspaces.Should().HaveCount(3);
        result.Value.WorkspaceSelection.Workspaces.Should().OnlyContain(w => !string.IsNullOrEmpty(w.Slug) && !string.IsNullOrEmpty(w.DisplayName));
        result.Value.WorkspaceSelection.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-07-24T12:05:00Z"));
        fixture.Continuation.Verify(
            c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "no tenant is bound yet until the user selects a workspace");
    }
}
