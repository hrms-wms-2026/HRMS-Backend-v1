using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

public class TrayEnrollmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_AddsActiveRegistrationAndHashedRefreshToken()
    {
        var repository = new Mock<ITrayActivationRepository>();
        var tokens = TokenService();
        var service = CreateService(repository, tokens);
        var request = Request();

        var result = await service.IssueAsync(request, CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("raw-refresh-token");
        repository.Verify(r => r.AddDeviceRegistrationAsync(
            It.Is<TrayDeviceRegistration>(d => d.TenantId == TenantId
                && d.UserId == UserId
                && d.LegalEntityId == LegalEntityId
                && d.IsActive
                && d.DeviceName == request.DeviceName
                && d.DeviceOs == request.DeviceOs
                && d.DeviceFingerprint == request.DeviceFingerprint),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddRefreshTokenAsync(
            It.Is<TrayDeviceRefreshToken>(t => t.TokenHash == "refresh-token-hash"
                && t.TenantId == TenantId
                && t.UserId == UserId
                && !t.IsRevoked),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_ReturnsTenantSlug()
    {
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService());

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.TenantSlug.Should().Be("acme");
    }

    [Fact]
    public async Task IssueAsync_ReturnsEmployeeProfile_WhenEmployeeExists()
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindEmployeeProfileAsync(
                UserId, TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayEmployeeProfile("Ada", "Lovelace", "ada@example.com", "EMP-001"));
        var service = CreateService(repository, TokenService());

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.EmployeeName.Should().Be("Ada Lovelace");
        result.EmployeeEmail.Should().Be("ada@example.com");
        result.EmployeeNumber.Should().Be("EMP-001");
    }

    [Fact]
    public async Task IssueAsync_FallsBackToUserIdentity_WhenEmployeeIsMissing()
    {
        var repository = new Mock<ITrayActivationRepository>();
        var userRepository = new Mock<IUserRepository>();
        userRepository.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = UserId, FirstName = "Grace", LastName = "Hopper", Email = "grace@example.com" });
        var service = CreateService(repository, TokenService(), userRepository);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.EmployeeName.Should().Be("Grace Hopper");
        result.EmployeeEmail.Should().Be("grace@example.com");
        result.EmployeeNumber.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_DoesNotCallSaveChanges_CallerOwnsTransaction()
    {
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService());

        await service.IssueAsync(Request(), CancellationToken.None);

        repository.Verify(r => r.AddDeviceRegistrationAsync(
            It.IsAny<TrayDeviceRegistration>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddRefreshTokenAsync(
            It.IsAny<TrayDeviceRefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenNoActiveDeviceExists_IssuesCredentialsNormally()
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveDeviceForUserAsync(UserId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRegistration?)null);
        var deviceChangeRequests = new Mock<IDeviceChangeRequestRepository>();
        var service = CreateService(repository, TokenService(), deviceChangeRequests: deviceChangeRequests);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        deviceChangeRequests.Verify(r => r.UpsertPendingAsync(
            It.IsAny<DeviceChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IssueAsync_WhenSameFingerprintAsActiveDevice_IssuesCredentialsNormally()
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveDeviceForUserAsync(UserId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = Guid.NewGuid(), UserId = UserId, TenantId = TenantId,
                DeviceFingerprint = "fingerprint", IsActive = true,
            });
        var deviceChangeRequests = new Mock<IDeviceChangeRequestRepository>();
        var service = CreateService(repository, TokenService(), deviceChangeRequests: deviceChangeRequests);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        deviceChangeRequests.Verify(r => r.UpsertPendingAsync(
            It.IsAny<DeviceChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IssueAsync_WhenDifferentFingerprintThanActiveDevice_RaisesRequestAndThrows()
    {
        var existingDeviceId = Guid.NewGuid();
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveDeviceForUserAsync(UserId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = existingDeviceId, UserId = UserId, TenantId = TenantId,
                DeviceFingerprint = "fp-old", IsActive = true,
            });
        DeviceChangeRequest? raised = null;
        var deviceChangeRequests = new Mock<IDeviceChangeRequestRepository>();
        deviceChangeRequests
            .Setup(r => r.UpsertPendingAsync(It.IsAny<DeviceChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceChangeRequest, CancellationToken>((r, _) => raised = r)
            .Returns(Task.CompletedTask);
        var service = CreateService(repository, TokenService(), deviceChangeRequests: deviceChangeRequests);

        var act = () => service.IssueAsync(Request(), CancellationToken.None);

        await act.Should().ThrowAsync<DeviceChangePendingException>();
        raised.Should().NotBeNull();
        raised!.Status.Should().Be(DeviceChangeRequest.StatusPending);
        raised.CurrentDeviceRegistrationId.Should().Be(existingDeviceId);
        raised.NewDeviceFingerprint.Should().Be("fingerprint");
        raised.LegalEntityId.Should().Be(LegalEntityId);
        deviceChangeRequests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenLegalCheckerReportsPending_SetsRequiresLegalAcceptanceAndDocuments()
    {
        var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc }));
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService(), legalChecker: legalChecker);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.RequiresLegalAcceptance.Should().BeTrue();
        result.PendingLegalDocuments.Should().ContainSingle();
    }

    [Fact]
    public async Task IssueAsync_WhenLegalCheckerReportsPending_IssuesLegalChallengeAndCsrfToken()
    {
        var pendingDoc = new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash");
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Pending, IsComplete: false, PendingDocuments: new[] { pendingDoc }));
        var legalChallenges = new Mock<ILegalLoginChallengeRepository>();
        legalChallenges.Setup(c => c.CreateAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("raw-challenge", "raw-csrf"));
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService(), legalChecker: legalChecker, legalChallenges: legalChallenges);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.LegalChallenge.Should().Be("raw-challenge");
        result.LegalCsrfToken.Should().Be("raw-csrf");
    }

    [Fact]
    public async Task IssueAsync_WhenLegalCheckerReportsComplete_LeavesRequiresLegalAcceptanceFalse()
    {
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.Complete, IsComplete: true, PendingDocuments: Array.Empty<PendingLegalDocumentDto>()));
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService(), legalChecker: legalChecker);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.RequiresLegalAcceptance.Should().BeFalse();
        (result.PendingLegalDocuments ?? Array.Empty<PendingLegalDocumentDto>()).Should().BeEmpty();
    }

    [Fact]
    public async Task IssueAsync_WhenLegalCheckerReportsNotConfigured_LeavesRequiresLegalAcceptanceFalse()
    {
        var legalChecker = new Mock<ILegalAcceptanceChecker>();
        legalChecker.Setup(c => c.CheckAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalAcceptanceCheckResult(
                LegalAcceptanceStatus.NotConfigured, IsComplete: false, PendingDocuments: Array.Empty<PendingLegalDocumentDto>(),
                ErrorCode: "MissingRequiredLegalVersions"));
        var repository = new Mock<ITrayActivationRepository>();
        var service = CreateService(repository, TokenService(), legalChecker: legalChecker);

        var result = await service.IssueAsync(Request(), CancellationToken.None);

        result.RequiresLegalAcceptance.Should().BeFalse();
    }

    [Fact]
    public async Task IssueAsync_WhenDifferentFingerprintAndRequestHasNoLegalEntity_FallsBackToEmployeesDefaultLegalEntity()
    {
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveDeviceForUserAsync(UserId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrayDeviceRegistration
            {
                Id = Guid.NewGuid(), UserId = UserId, TenantId = TenantId,
                DeviceFingerprint = "fp-old", IsActive = true,
            });
        var fallbackLegalEntityId = Guid.NewGuid();
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.CoreHr.Entities.Employee { LegalEntityId = fallbackLegalEntityId });
        DeviceChangeRequest? raised = null;
        var deviceChangeRequests = new Mock<IDeviceChangeRequestRepository>();
        deviceChangeRequests
            .Setup(r => r.UpsertPendingAsync(It.IsAny<DeviceChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceChangeRequest, CancellationToken>((r, _) => raised = r)
            .Returns(Task.CompletedTask);
        var requestWithoutLegalEntity = new TrayEnrollmentRequest(
            TenantId, UserId, null, "DESKTOP-7K2Q", "Windows 11", "fingerprint");
        var service = CreateService(repository, TokenService(), deviceChangeRequests: deviceChangeRequests, employees: employees);

        var act = () => service.IssueAsync(requestWithoutLegalEntity, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceChangePendingException>();
        raised.Should().NotBeNull();
        raised!.LegalEntityId.Should().Be(fallbackLegalEntityId);
    }

    private static TrayEnrollmentRequest Request() => new(
        TenantId,
        UserId,
        LegalEntityId,
        "DESKTOP-7K2Q",
        "Windows 11",
        "fingerprint");

    private static TrayEnrollmentService CreateService(
        Mock<ITrayActivationRepository> repository,
        Mock<ITrayTokenService> tokens,
        Mock<IUserRepository>? userRepository = null,
        Mock<IDeviceChangeRequestRepository>? deviceChangeRequests = null,
        Mock<IEmployeeRepository>? employees = null,
        Mock<ILegalAcceptanceChecker>? legalChecker = null,
        Mock<ILegalLoginChallengeRepository>? legalChallenges = null)
    {
        var tenantRepository = new Mock<ITenantRepository>();
        tenantRepository.Setup(r => r.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Slug = "acme" });

        return new TrayEnrollmentService(
            repository.Object,
            userRepository?.Object ?? new Mock<IUserRepository>().Object,
            tenantRepository.Object,
            new Mock<ITenantContextSwitcher>().Object,
            tokens.Object,
            new Mock<IDateTimeProvider>().Object,
            deviceChangeRequests?.Object ?? new Mock<IDeviceChangeRequestRepository>().Object,
            employees?.Object ?? new Mock<IEmployeeRepository>().Object,
            legalChecker?.Object ?? DefaultLegalChecker().Object,
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

    private static Mock<ITrayTokenService> TokenService()
    {
        var tokens = new Mock<ITrayTokenService>();
        tokens.Setup(t => t.GenerateRawRefreshToken()).Returns("raw-refresh-token");
        tokens.Setup(t => t.HashToken("raw-refresh-token")).Returns("refresh-token-hash");
        tokens.Setup(t => t.GenerateAccessToken(
            It.IsAny<Guid>(), UserId, TenantId, LegalEntityId)).Returns("access-token");
        return tokens;
    }
}
