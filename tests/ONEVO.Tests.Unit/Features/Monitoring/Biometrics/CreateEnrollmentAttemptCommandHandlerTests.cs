using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CreateEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IEmployeeIdentityResolver> _employeeResolver = new();
    private readonly Mock<IBiometricVerificationProvider> _provider = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private CreateEnrollmentAttemptCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _employeeResolver.Object, _provider.Object, _clock.Object, _unitOfWork.Object);

    private void SetupAuthenticatedDevice()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(_deviceId);
    }

    private Tenant Tenant() => new() { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active };

    [Fact]
    public async Task Handle_WithoutAuthenticatedDevice_Returns401()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(false);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmployeeCannotBeResolved_Returns422()
    {
        SetupAuthenticatedDevice();
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default)).ReturnsAsync(Tenant());
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, default))
            .ReturnsAsync((Guid?)null);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithResolvedEmployee_CreatesAttemptAndReturnsAwsSession()
    {
        SetupAuthenticatedDevice();
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default)).ReturnsAsync(Tenant());
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, default))
            .ReturnsAsync(_employeeId);
        _provider.Setup(p => p.CreateLivenessSessionAsync(It.IsAny<CreateLivenessSessionRequest>(), default))
            .ReturnsAsync(new FaceLivenessSessionCreated("aws-session-123"));
        _provider.Setup(p => p.IssueScopedCaptureCredentialsAsync("aws-session-123", default))
            .ReturnsAsync(new ScopedCaptureCredentials("AKIA...", "secret", "token", "ap-south-1", DateTimeOffset.UtcNow.AddMinutes(15)));
        var now = DateTimeOffset.UtcNow;
        _clock.SetupGet(c => c.UtcNow).Returns(now);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("aws-session-123", result.Value!.AwsSessionId);
        Assert.Equal("ap-south-1", result.Value.Region);
        _repository.Verify(r => r.AddAttemptAsync(
            It.Is<BiometricVerificationAttempt>(a =>
                a.TenantId == _tenantId && a.EmployeeId == _employeeId && a.UserId == _userId
                && a.Purpose == BiometricAttemptPurpose.Enrollment
                && a.AwsSessionId == "aws-session-123"),
            default), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
