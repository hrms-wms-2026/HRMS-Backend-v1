using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CompleteEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IBiometricVerificationProvider> _provider = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();

    private CompleteEnrollmentAttemptCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _provider.Object, _fileStorage.Object, _clock.Object, _unitOfWork.Object);

    private void SetupAuthenticatedDevice()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(_deviceId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active });
    }

    private BiometricVerificationAttempt AttemptInStatus(string status) => new()
    {
        Id = _attemptId,
        TenantId = _tenantId,
        EmployeeId = _employeeId,
        UserId = _userId,
        DeviceRegistrationId = _deviceId,
        Purpose = BiometricAttemptPurpose.Enrollment,
        AwsSessionId = "aws-session-123",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_WhenAttemptNotFound_ReturnsNotFound()
    {
        SetupAuthenticatedDevice();
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default))
            .ReturnsAsync((BiometricVerificationAttempt?)null);

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenAwsSessionStillInProgress_ReturnsConflict()
    {
        SetupAuthenticatedDevice();
        var attempt = AttemptInStatus(BiometricAttemptStatus.Capturing);
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default)).ReturnsAsync(attempt);
        _provider.Setup(p => p.GetLivenessSessionResultAsync("aws-session-123", default))
            .ReturnsAsync(new FaceLivenessSessionResult("IN_PROGRESS", null, null));

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenLivenessSucceeds_CreatesProfileAndSupersedesPrevious()
    {
        SetupAuthenticatedDevice();
        var attempt = AttemptInStatus(BiometricAttemptStatus.Capturing);
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default)).ReturnsAsync(attempt);
        var referenceBytes = new byte[] { 1, 2, 3 };
        _provider.Setup(p => p.GetLivenessSessionResultAsync("aws-session-123", default))
            .ReturnsAsync(new FaceLivenessSessionResult("SUCCEEDED", 98.5, referenceBytes));
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), "image/jpeg",
                ONEVO.Application.Features.Storage.File.Helpers.UploadPurposeCatalog.MonitoringFaceLiveness,
                It.IsAny<Stream>(), default))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                Guid.NewGuid(), _tenantId, "tenants/x/files/y/ref.jpg", "enrollment-reference.jpg",
                "ref.jpg", "image/jpeg", referenceBytes.Length, "checksum", "available", DateTimeOffset.UtcNow)));
        var now = DateTimeOffset.UtcNow;
        _clock.SetupGet(c => c.UtcNow).Returns(now);

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(BiometricProfileStatus.Active, result.Value!.Status);
        _repository.Verify(r => r.SupersedeActiveProfileAsync(_employeeId, _tenantId, now, default), Times.Once);
        _repository.Verify(r => r.AddProfileAsync(
            It.Is<EmployeeBiometricProfile>(p => p.EmployeeId == _employeeId && p.Status == BiometricProfileStatus.Active),
            default), Times.Once);
        Assert.Equal(BiometricAttemptStatus.Verified, attempt.Status);
    }
}
