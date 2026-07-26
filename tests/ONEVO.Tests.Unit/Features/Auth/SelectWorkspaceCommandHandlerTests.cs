using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class SelectWorkspaceCommandHandlerTests
{
    private sealed class SelectWorkspaceHandlerFixture
    {
        public Mock<ILoginWorkspaceSelectionChallengeRepository> WorkspaceChallenges { get; } = new();
        public Mock<ITenantContextSwitcher> TenantSwitcher { get; } = new();
        public Mock<ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces.ITenantRepository> Tenants { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<ILoginContinuationService> Continuation { get; } = new();

        public SelectWorkspaceCommandHandler Build() => new(
            WorkspaceChallenges.Object,
            TenantSwitcher.Object,
            Tenants.Object,
            Users.Object,
            Continuation.Object);
    }

    [Fact]
    public async Task Handle_WithUnknownChallenge_ReturnsGenericSelectionFailure()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        fixture.WorkspaceChallenges
            .Setup(r => r.GetActiveAsync("unknown-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LoginWorkspaceSelectionChallengeState?)null);

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("unknown-challenge", "acme", "127.0.0.1", "test-agent"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.WorkspaceChallenges.Verify(
            r => r.RegisterFailedAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unknown challenge is not an invalid slug choice against a real challenge");
    }

    [Fact]
    public async Task Handle_WithSlugNotInCandidateSnapshot_RegistersFailedAttempt_AndReturnsGenericFailure()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        var challengeState = new LoginWorkspaceSelectionChallengeState(
            "user@example.com",
            new[] { new WorkspaceCandidateSnapshot(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test") },
            DateTimeOffset.Parse("2026-07-24T12:05:00Z"),
            0);
        fixture.WorkspaceChallenges
            .Setup(r => r.GetActiveAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("raw-challenge", "not-a-real-slug", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.WorkspaceChallenges.Verify(
            r => r.RegisterFailedAttemptAsync("raw-challenge", 5, It.IsAny<CancellationToken>()), Times.Once);
        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithValidSlug_ButTenantNoLongerEligible_ConsumesChallenge_AndReturnsGenericFailure()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var challengeState = new LoginWorkspaceSelectionChallengeState(
            "user@example.com",
            new[] { new WorkspaceCandidateSnapshot(tenantId, userId, "acme", "Acme Test") },
            DateTimeOffset.Parse("2026-07-24T12:05:00Z"),
            0);
        var suspendedTenant = new ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant
        {
            Id = tenantId, Name = "Acme Test", Slug = "acme", CompanySizeRange = "51-200",
            Status = ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Suspended
        };
        fixture.WorkspaceChallenges.Setup(r => r.GetActiveAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(suspendedTenant);

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("raw-challenge", "acme", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.WorkspaceChallenges.Verify(r => r.TryConsumeAsync("raw-challenge", It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkspaceChallenges.Verify(
            r => r.RegisterFailedAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "revalidation failure is a terminal outcome, not an invalid-slug attempt");
    }

    [Fact]
    public async Task Handle_WithValidSlug_ButUserNoLongerActive_ConsumesChallenge_AndReturnsGenericFailure()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        var tenant = new ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant
        {
            Id = Guid.NewGuid(), Name = "Acme Test", Slug = "acme", CompanySizeRange = "51-200",
            Status = ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active
        };
        var user = new ONEVO.Domain.Features.InfrastructureModule.Entities.User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "user@example.com",
            PasswordHash = "irrelevant", FirstName = "Jane", LastName = "Doe", IsActive = false
        };
        var challengeState = new LoginWorkspaceSelectionChallengeState(
            "user@example.com",
            new[] { new WorkspaceCandidateSnapshot(tenant.Id, user.Id, "acme", "Acme Test") },
            DateTimeOffset.Parse("2026-07-24T12:05:00Z"),
            0);
        fixture.WorkspaceChallenges.Setup(r => r.GetActiveAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("raw-challenge", "acme", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Sign-in selection expired. Please sign in again.");
        fixture.WorkspaceChallenges.Verify(r => r.TryConsumeAsync("raw-challenge", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidSlug_ButConsumeRaceIsLost_ReturnsGenericFailure()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        var tenant = new ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant
        {
            Id = Guid.NewGuid(), Name = "Acme Test", Slug = "acme", CompanySizeRange = "51-200",
            Status = ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active
        };
        var user = new ONEVO.Domain.Features.InfrastructureModule.Entities.User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "user@example.com",
            PasswordHash = "irrelevant", FirstName = "Jane", LastName = "Doe", IsActive = true
        };
        var challengeState = new LoginWorkspaceSelectionChallengeState(
            "user@example.com",
            new[] { new WorkspaceCandidateSnapshot(tenant.Id, user.Id, "acme", "Acme Test") },
            DateTimeOffset.Parse("2026-07-24T12:05:00Z"),
            0);
        fixture.WorkspaceChallenges.Setup(r => r.GetActiveAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.WorkspaceChallenges
            .Setup(r => r.TryConsumeAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LoginWorkspaceSelectionChallengeState?)null);

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("raw-challenge", "acme", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        fixture.Continuation.Verify(
            c => c.ContinueAsync(It.IsAny<LoginContinuationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "a lost consume race must never proceed to login continuation");
    }

    [Fact]
    public async Task Handle_WithValidSlug_ConsumesChallenge_AndDelegatesToContinuationWithoutRepeatingTheSwitch()
    {
        var fixture = new SelectWorkspaceHandlerFixture();
        var tenant = new ONEVO.Domain.Features.InfrastructureModule.Entities.Tenant
        {
            Id = Guid.NewGuid(), Name = "Acme Test", Slug = "acme", CompanySizeRange = "51-200",
            Status = ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active
        };
        var user = new ONEVO.Domain.Features.InfrastructureModule.Entities.User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "user@example.com",
            PasswordHash = "irrelevant", FirstName = "Jane", LastName = "Doe", IsActive = true
        };
        var challengeState = new LoginWorkspaceSelectionChallengeState(
            "user@example.com",
            new[] { new WorkspaceCandidateSnapshot(tenant.Id, user.Id, "acme", "Acme Test") },
            DateTimeOffset.Parse("2026-07-24T12:05:00Z"),
            0);
        var sessionDto = new LoginResponseDto("csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(user.Id, user.TenantId, user.Email));
        fixture.WorkspaceChallenges.Setup(r => r.GetActiveAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.WorkspaceChallenges.Setup(r => r.TryConsumeAsync("raw-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challengeState);
        fixture.Continuation
            .Setup(c => c.ContinueAsync(
                It.Is<LoginContinuationRequest>(r =>
                    r.TenantId == tenant.Id && r.UserId == user.Id && !r.SwitchTenantContext),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var result = await fixture.Build().Handle(
            new SelectWorkspaceCommand("raw-challenge", "acme", "127.0.0.1", "test-agent"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sessionDto);
        fixture.WorkspaceChallenges.Verify(r => r.TryConsumeAsync("raw-challenge", It.IsAny<CancellationToken>()), Times.Once);
        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()), Times.Once,
            "the handler itself switches once, before consuming the challenge; continuation is told not to switch again");
    }
}
