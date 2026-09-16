using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation.Commands;

public sealed class ExchangeActivationCodeCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenEnrollmentServiceThrowsDeviceChangePending_ReturnsDeviceChangePendingFailure()
    {
        var activationCode = new TrayActivationCode
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = UserId,
            CodeHash = "code-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        var repository = new Mock<ITrayActivationRepository>();
        repository.Setup(r => r.FindActiveCodeByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activationCode);
        var tokenService = new Mock<ITrayTokenService>();
        tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns("code-hash");
        var enrollmentService = new Mock<ITrayEnrollmentService>();
        enrollmentService
            .Setup(s => s.IssueAsync(It.IsAny<TrayEnrollmentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeviceChangePendingException());
        var unitOfWork = CreateUnitOfWork();
        var handler = new ExchangeActivationCodeCommandHandler(
            repository.Object, tokenService.Object, enrollmentService.Object, unitOfWork.Object);

        var result = await handler.Handle(
            new ExchangeActivationCodeCommand("ABC123", "DESKTOP-1", "Windows 11", "fingerprint"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("device_change_pending");
        result.StatusCode.Should().Be(409);
    }

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
