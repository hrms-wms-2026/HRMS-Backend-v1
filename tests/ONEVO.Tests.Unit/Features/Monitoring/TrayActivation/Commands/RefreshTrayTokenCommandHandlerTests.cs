using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation.Commands;

public sealed class RefreshTrayTokenCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DeviceRegistrationId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenLegalCheckerReportsPending_SetsRequiresLegalAcceptanceOnRefreshResponse()
    {
        var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc }));

        var repository = CreateRepository();
        var handler = CreateHandler(repository, legalChecker: legalChecker);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresLegalAcceptance.Should().BeTrue();
        result.Value!.PendingLegalDocuments.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenLegalCheckerReportsPending_IssuesLegalChallengeAndCsrfToken()
    {
        var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc }));
        var legalChallenges = new Mock<ILegalLoginChallengeRepository>();
        legalChallenges.Setup(c => c.CreateAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("raw-challenge", "raw-csrf"));

        var repository = CreateRepository();
        var handler = CreateHandler(repository, legalChecker: legalChecker, legalChallenges: legalChallenges);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.Value!.LegalChallenge.Should().Be("raw-challenge");
        result.Value!.LegalCsrfToken.Should().Be("raw-csrf");
    }

    [Fact]
    public async Task Handle_WhenLegalCheckerReportsComplete_LeavesRequiresLegalAcceptanceFalse()
    {
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Complete, IsComplete: true, PendingDocuments: Array.Empty<PendingLegalDocumentDto>()));

        var repository = CreateRepository();
        var handler = CreateHandler(repository, legalChecker: legalChecker);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresLegalAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenLegalCheckerReportsNotConfigured_LeavesRequiresLegalAcceptanceFalse()
    {
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.NotConfigured, IsComplete: false, PendingDocuments: Array.Empty<PendingLegalDocumentDto>(),
                ErrorCode: "MissingRequiredLegalVersions"));

        var repository = CreateRepository();
        var handler = CreateHandler(repository, legalChecker: legalChecker);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresLegalAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenRotatedTokenIsRetriedWithinGrace_IssuesNewTokenAndRevokesUnreceivedReplacement()
    {
        var repository = CreateRepository();
        repository.Setup(r => r.FindActiveRefreshTokenAsync("refresh-token-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRefreshToken?)null);
        repository.Setup(r => r.FindUnconsumedRotatedRefreshTokenAsync(
                "refresh-token-hash", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = UserId,
                DeviceRegistrationId = DeviceRegistrationId,
                TokenHash = "refresh-token-hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                IsRevoked = true,
                RevokedReason = TrayDeviceRefreshToken.RotatedReason,
            });
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().Be("new-raw-refresh-token");
        repository.Verify(r => r.RevokeAllRefreshTokensForDeviceAsync(
            DeviceRegistrationId, "rotation_retry", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddRefreshTokenAsync(It.IsAny<TrayDeviceRefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTokenIsNeitherActiveNorAnUnconsumedRotation_Returns401()
    {
        var repository = CreateRepository();
        repository.Setup(r => r.FindActiveRefreshTokenAsync("refresh-token-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRefreshToken?)null);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(
            new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        repository.Verify(r => r.AddRefreshTokenAsync(It.IsAny<TrayDeviceRefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NormalRotation_RevokesOnlyThePresentedToken()
    {
        var repository = CreateRepository();
        var handler = CreateHandler(repository);

        await handler.Handle(new RefreshTrayTokenCommand("raw-refresh-token", "fingerprint"), CancellationToken.None);

        repository.Verify(r => r.RevokeRefreshTokenAsync(
            It.IsAny<TrayDeviceRefreshToken>(), TrayDeviceRefreshToken.RotatedReason, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RevokeAllRefreshTokensForDeviceAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<ITrayActivationRepository> CreateRepository()
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveRefreshTokenAsync("refresh-token-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                UserId = UserId,
                DeviceRegistrationId = DeviceRegistrationId,
                TokenHash = "refresh-token-hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                IsRevoked = false,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        repository.Setup(r => r.FindActiveDeviceAsync(DeviceRegistrationId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = DeviceRegistrationId,
                TenantId = TenantId,
                UserId = UserId,
                DeviceFingerprint = "fingerprint",
                IsActive = true,
            });
        return repository;
    }

    private static RefreshTrayTokenCommandHandler CreateHandler(
        Mock<ITrayActivationRepository> repository,
        Mock<ILegalAcceptanceChecker>? legalChecker = null,
        Mock<ILegalLoginChallengeRepository>? legalChallenges = null)
    {
        var tokenService = new Mock<ITrayTokenService>();
        tokenService.Setup(t => t.HashToken("raw-refresh-token")).Returns("refresh-token-hash");
        tokenService.Setup(t => t.GenerateRawRefreshToken()).Returns("new-raw-refresh-token");
        tokenService.Setup(t => t.GenerateAccessToken(
            DeviceRegistrationId, UserId, TenantId, It.IsAny<Guid?>())).Returns("access-token");

        var tenantRepository = new Mock<ITenantRepository>();
        tenantRepository.Setup(r => r.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Slug = "acme" });

        var resolvedLegalChecker = legalChecker ?? DefaultLegalChecker();
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        return new RefreshTrayTokenCommandHandler(
            repository.Object,
            new Mock<IUserRepository>().Object,
            tenantRepository.Object,
            new Mock<ITenantContextSwitcher>().Object,
            tokenService.Object,
            clock.Object,
            new Mock<IUnitOfWork>().Object,
            resolvedLegalChecker.Object,
            legalChallenges?.Object ?? new Mock<ILegalLoginChallengeRepository>().Object);
    }

    private static Mock<ILegalAcceptanceChecker> DefaultLegalChecker()
    {
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Complete, IsComplete: true, PendingDocuments: Array.Empty<PendingLegalDocumentDto>()));
        return legalChecker;
    }
}
