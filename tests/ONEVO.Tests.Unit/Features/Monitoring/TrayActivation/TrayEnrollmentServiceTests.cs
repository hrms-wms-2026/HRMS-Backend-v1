using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
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
        Mock<IUserRepository>? userRepository = null)
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
            new Mock<IDateTimeProvider>().Object);
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
