using System.Security.Cryptography;
using System.Text;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class CompleteEnrollmentCommandHandlerTests
{
    private readonly Mock<IAgentGatewayRepository> _repo = new();
    private readonly Mock<IUserProfileRepository> _profiles = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private CompleteEnrollmentCommandHandler CreateHandler() =>
        new(_repo.Object, _profiles.Object, _jwt.Object, _uow.Object);

    [Fact]
    public async Task Handle_FirstDevice_ActivatesDeviceAndCreatesSession()
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
        _repo.Setup(r => r.GetActiveAgentByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
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
        _profiles.Setup(p => p.GetEmployeeByIdAsync(employeeId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new Employee
             {
                 Id = employeeId,
                 TenantId = tenantId,
                 FirstName = "Jane",
                 LastName = "Doe"
             });

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
        Assert.Equal("Jane Doe", result.Value.EmployeeName);
        Assert.Equal("approved", result.Value.DeviceApprovalStatus);
        Assert.Null(result.Value.DeviceChangeRequestId);
        _repo.Verify(r => r.AddAgentAsync(
            It.Is<RegisteredAgent>(agent => agent.Status == "active"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddSessionAsync(
            It.Is<AgentSession>(session => session.EmployeeId == employeeId && session.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddDeviceChangeRequestAsync(
            It.IsAny<AgentDeviceChangeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReplacementDevice_CreatesPendingRequestWithoutSessionOrPolicy()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        const string plainCode = "valid-auth-code";
        var currentAgent = new RegisteredAgent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            DeviceId = "approved-device",
            Status = "active"
        };
        RegisteredAgent? capturedCandidate = null;
        AgentDeviceChangeRequest? capturedRequest = null;

        _repo.Setup(r => r.GetChallengeByIdAsync(enrollmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentEnrollmentChallenge
            {
                Id = enrollmentId,
                DeviceId = "replacement-device",
                DeviceName = "DESKTOP-NEW",
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
        _repo.Setup(r => r.GetAgentByDeviceIdAsync("replacement-device", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredAgent?)null);
        _repo.Setup(r => r.GetActiveAgentByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentAgent);
        _repo.Setup(r => r.GetPendingDeviceChangeByEmployeeIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentDeviceChangeRequest?)null);
        _repo.Setup(r => r.AddAgentAsync(It.IsAny<RegisteredAgent>(), It.IsAny<CancellationToken>()))
            .Callback<RegisteredAgent, CancellationToken>((agent, _) => capturedCandidate = agent)
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddDeviceChangeRequestAsync(
                It.IsAny<AgentDeviceChangeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentDeviceChangeRequest, CancellationToken>((request, _) => capturedRequest = request)
            .Returns(Task.CompletedTask);
        _profiles.Setup(p => p.GetEmployeeByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = employeeId,
                TenantId = tenantId,
                FirstName = "Jane",
                LastName = "Doe"
            });
        _jwt.Setup(j => j.GenerateAgentToken(It.IsAny<Guid>(), tenantId))
            .Returns("pending-device-token");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentCommand(enrollmentId, "replacement-device", plainCode),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pending", result.Value!.DeviceApprovalStatus);
        Assert.NotNull(result.Value.DeviceChangeRequestId);
        Assert.NotNull(capturedCandidate);
        Assert.Equal("inactive", capturedCandidate.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal(currentAgent.Id, capturedRequest.CurrentAgentId);
        Assert.Equal(capturedCandidate.Id, capturedRequest.RequestedAgentId);
        Assert.Equal("pending", capturedRequest.Status);
        Assert.Equal(capturedRequest.Id, result.Value.DeviceChangeRequestId);
        _repo.Verify(r => r.AddSessionAsync(
            It.IsAny<AgentSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.AddOrUpdatePolicyAsync(
            It.IsAny<AgentPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
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
