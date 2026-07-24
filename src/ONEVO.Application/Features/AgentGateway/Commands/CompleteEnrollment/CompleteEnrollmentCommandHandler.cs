using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteEnrollment;

public class CompleteEnrollmentCommandHandler
    : IRequestHandler<CompleteEnrollmentCommand, Result<EnrollCompleteResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUserProfileRepository _profiles;
    private readonly IJwtTokenService _jwt;
    private readonly IUnitOfWork _uow;

    private static readonly string DefaultPolicyJson = """
        {
          "activity_monitoring": false,
          "application_tracking": false,
          "screenshot_capture": false,
          "heartbeat_interval_seconds": 60
        }
        """;

    public CompleteEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        IUserProfileRepository profiles,
        IJwtTokenService jwt,
        IUnitOfWork uow)
    {
        _repo = repo;
        _profiles = profiles;
        _jwt = jwt;
        _uow = uow;
    }

    public async Task<Result<EnrollCompleteResponseDto>> Handle(
        CompleteEnrollmentCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _repo.GetChallengeByIdAsync(request.EnrollmentId, cancellationToken);

        if (challenge is null)
            return Result<EnrollCompleteResponseDto>.NotFound("Enrollment challenge not found.");

        if (challenge.ExpiresAt < DateTimeOffset.UtcNow)
            return Result<EnrollCompleteResponseDto>.Failure("Enrollment challenge has expired.", 400);

        if (challenge.Status != "confirmed")
            return Result<EnrollCompleteResponseDto>.Failure("Challenge has not been confirmed in the browser.", 400);

        if (!string.Equals(challenge.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
            return Result<EnrollCompleteResponseDto>.Failure("device_id does not match the enrollment challenge.", 401);

        var submittedHash = HashCode(request.AuthorizationCode);
        if (!string.Equals(submittedHash, challenge.AuthorizationCodeHash, StringComparison.OrdinalIgnoreCase))
            return Result<EnrollCompleteResponseDto>.Failure("Invalid authorization_code.", 401);

        // Atomic complete — prevents double-use of auth code
        var completed = await _repo.TryMarkChallengeCompletedAsync(request.EnrollmentId, cancellationToken);
        if (!completed)
            return Result<EnrollCompleteResponseDto>.Conflict("Enrollment was already completed.");

        var tenantId = challenge.TenantId!.Value;
        var employeeId = challenge.EmployeeId!.Value;
        var now = DateTimeOffset.UtcNow;

        var currentApproved = await _repo.GetActiveAgentByEmployeeIdAsync(
            employeeId, cancellationToken);

        // Create or refresh this installation's registered agent.
        var existing = await _repo.GetAgentByDeviceIdAsync(request.DeviceId, cancellationToken);
        RegisteredAgent agent;
        var isNewAgent = existing is null;
        if (existing is not null)
        {
            agent = existing;
            existing.DeviceName = challenge.DeviceName;
            existing.OsVersion = challenge.OsVersion;
            existing.AgentVersion = challenge.AgentVersion;
            existing.EmployeeId = employeeId;
            existing.UpdatedAt = now;
        }
        else
        {
            agent = new RegisteredAgent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                DeviceId = challenge.DeviceId,
                DeviceName = challenge.DeviceName,
                OsVersion = challenge.OsVersion,
                AgentVersion = challenge.AgentVersion,
                Status = "inactive",
                RegisteredAt = now,
                CreatedAt = now
            };
        }

        var sameApprovedDevice = currentApproved is not null &&
            string.Equals(
                currentApproved.DeviceId,
                request.DeviceId,
                StringComparison.OrdinalIgnoreCase);

        var approvalStatus = "approved";
        Guid? deviceChangeRequestId = null;

        if (currentApproved is null || sameApprovedDevice)
        {
            agent.Status = "active";

            if (isNewAgent)
                await _repo.AddAgentAsync(agent, cancellationToken);

            await _repo.EndActiveSessionAsync(request.DeviceId, now, cancellationToken);
            await _repo.AddSessionAsync(new AgentSession
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DeviceId = request.DeviceId,
                EmployeeId = employeeId,
                IsActive = true,
                CreatedAt = now
            }, cancellationToken);

            await _repo.AddOrUpdatePolicyAsync(new AgentPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgentId = agent.Id,
                PolicyJson = DefaultPolicyJson,
                CreatedAt = now
            }, cancellationToken);
        }
        else
        {
            agent.Status = "inactive";
            approvalStatus = "pending";

            if (isNewAgent)
                await _repo.AddAgentAsync(agent, cancellationToken);

            var pending = await _repo.GetPendingDeviceChangeByEmployeeIdAsync(
                employeeId, cancellationToken);
            if (pending is null)
            {
                pending = new AgentDeviceChangeRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    CurrentAgentId = currentApproved.Id,
                    RequestedAgentId = agent.Id,
                    Status = "pending",
                    RequestedAt = now,
                    CreatedAt = now
                };
                await _repo.AddDeviceChangeRequestAsync(pending, cancellationToken);
            }

            deviceChangeRequestId = pending.Id;
        }

        await _uow.SaveChangesAsync(cancellationToken);

        var employee = await _profiles.GetEmployeeByIdAsync(employeeId, cancellationToken);
        var employeeName = employee is null
            ? string.Empty
            : $"{employee.FirstName} {employee.LastName}".Trim();

        var deviceToken = _jwt.GenerateAgentToken(agent.Id, tenantId);
        var tokenExpiresAt = DateTimeOffset.UtcNow.AddDays(90);

        return Result<EnrollCompleteResponseDto>.Success(new EnrollCompleteResponseDto(
            AgentId: agent.Id,
            TenantId: tenantId,
            EmployeeId: employeeId,
            EmployeeName: employeeName,
            DeviceToken: deviceToken,
            TokenExpiresAt: tokenExpiresAt,
            PolicyJson: DefaultPolicyJson,
            DeviceApprovalStatus: approvalStatus,
            DeviceChangeRequestId: deviceChangeRequestId));
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
