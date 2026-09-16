using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation.Commands;

public sealed class PollDeviceAuthorizationCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenEnrollmentServiceThrowsDeviceChangePending_ReturnsDeviceChangePendingFailure()
    {
        var authorization = ApprovedAuthorization();
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.LockDeviceAuthorizationForPollAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorization);
        var tokenService = new Mock<ITrayTokenService>();
        tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string s) => s);
        var enrollmentService = new Mock<ITrayEnrollmentService>();
        enrollmentService
            .Setup(s => s.IssueAsync(It.IsAny<TrayEnrollmentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeviceChangePendingException());
        var unitOfWork = CreateUnitOfWork();
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var handler = new PollDeviceAuthorizationCommandHandler(
            repository.Object, tokenService.Object, enrollmentService.Object,
            clock.Object, unitOfWork.Object);

        var result = await handler.Handle(
            new PollDeviceAuthorizationCommand("device-code", "fingerprint"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("device_change_pending");
        authorization.Status.Should().Be(DeviceAuthorizationStatus.Consumed);
    }

    private static TrayDeviceAuthorization ApprovedAuthorization() => new()
    {
        Id = Guid.NewGuid(),
        Status = DeviceAuthorizationStatus.Approved,
        ApprovedTenantId = TenantId,
        ApprovedUserId = UserId,
        DeviceFingerprintHash = "fingerprint",
        ExpiresAt = Now.AddMinutes(5),
        DeviceName = "DESKTOP-1",
        DeviceOs = "Windows 11",
    };

    private static Mock<IUnitOfWork> CreateUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<TrayAuthResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TrayAuthResponseDto>>> operation, CancellationToken ct) => operation(ct));
        return unitOfWork;
    }
}
