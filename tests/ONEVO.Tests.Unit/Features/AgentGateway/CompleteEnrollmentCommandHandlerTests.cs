using System.Security.Cryptography;
using System.Text;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class CompleteEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private CompleteEnrollmentCommandHandler CreateHandler() =>
        new(_repo.Object, _jwt.Object, _uow.Object);

    [Fact]
    public async Task Handle_ValidCode_ReturnsDeviceToken()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        const string plainCode = "valid-auth-code";

        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 DeviceId = "device-uuid-v7",
                 DeviceName = "DESKTOP-ABC",
                 OsVersion = "Windows 11",
                 AgentVersion = "1.0.0",
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash(plainCode),
                 TenantId = tenantId,
                 EmployeeId = employeeId,
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
             });

        _repo.Setup(r => r.TryMarkChallengeCompletedAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        _repo.Setup(r => r.GetAgentByDeviceIdAsync("device-uuid-v7", It.IsAny<CancellationToken>()))
             .ReturnsAsync((RegisteredAgent?)null);
        _repo.Setup(r => r.AddAgentAsync(It.IsAny<RegisteredAgent>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.EndActiveSessionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddSessionAsync(It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddOrUpdatePolicyAsync(It.IsAny<AgentPolicy>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _jwt.Setup(j => j.GenerateAgentToken(It.IsAny<Guid>(), tenantId))
            .Returns("eyJ.test.token");

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-uuid-v7", plainCode),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("eyJ.test.token", result.Value!.DeviceToken);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
    }

    [Fact]
    public async Task Handle_WrongAuthCode_ReturnsUnauthorized()
    {
        var enrollmentId = Guid.NewGuid();
        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 DeviceId = "device-id",
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash("correct-code"),
                 TenantId = Guid.NewGuid(),
                 EmployeeId = Guid.NewGuid(),
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
             });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-id", "wrong-code"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ExpiredChallenge_ReturnsFailure()
    {
        var enrollmentId = Guid.NewGuid();
        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new AgentEnrollmentChallenge
             {
                 Id = enrollmentId,
                 DeviceId = "device-id",
                 Status = "confirmed",
                 AuthorizationCodeHash = Hash("code"),
                 TenantId = Guid.NewGuid(),
                 EmployeeId = Guid.NewGuid(),
                 ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) // expired
             });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new CompleteEnrollmentCommand(enrollmentId, "device-id", "code"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
