using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.ApproveDeviceChange;

public sealed class ApproveDeviceChangeCommandHandler
    : IRequestHandler<ApproveDeviceChangeCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    private static readonly string DefaultPolicyJson = """
        {
          "activity_monitoring": false,
          "application_tracking": false,
          "screenshot_capture": false,
          "heartbeat_interval_seconds": 60
        }
        """;

    public ApproveDeviceChangeCommandHandler(
        IAgentGatewayRepository repo,
        IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(
        ApproveDeviceChangeCommand command,
        CancellationToken cancellationToken)
    {
        var reviewComment = command.ReviewComment?.Trim();
        if (reviewComment?.Length > 500)
            return Result.Failure("Review comment cannot exceed 500 characters.");

        var request = await _repo.GetDeviceChangeRequestByIdAsync(
            command.RequestId, cancellationToken);
        if (request is null)
            return Result.NotFound("Device change request not found.");
        if (!string.Equals(request.Status, "pending", StringComparison.Ordinal))
            return Result.Conflict("Device change request is no longer pending.");

        var current = await _repo.GetAgentByIdAsync(
            request.CurrentAgentId, cancellationToken);
        var candidate = await _repo.GetAgentByIdAsync(
            request.RequestedAgentId, cancellationToken);

        if (current is null ||
            candidate is null ||
            current.TenantId != request.TenantId ||
            candidate.TenantId != request.TenantId ||
            current.EmployeeId != request.EmployeeId ||
            candidate.EmployeeId != request.EmployeeId ||
            !string.Equals(current.Status, "active", StringComparison.Ordinal) ||
            !string.Equals(candidate.Status, "inactive", StringComparison.Ordinal))
        {
            return Result.Conflict("Device bindings changed; refresh and retry.");
        }

        var now = DateTimeOffset.UtcNow;
        current.Status = "revoked";
        current.UpdatedAt = now;
        candidate.Status = "active";
        candidate.UpdatedAt = now;

        request.Status = "approved";
        request.ReviewedById = command.ReviewedById;
        request.ReviewedAt = now;
        request.ReviewComment = reviewComment;
        request.UpdatedAt = now;

        await _repo.EndActiveSessionAsync(current.DeviceId, now, cancellationToken);
        await _repo.AddSessionAsync(new AgentSession
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            DeviceId = candidate.DeviceId,
            EmployeeId = request.EmployeeId,
            IsActive = true,
            CreatedAt = now
        }, cancellationToken);
        await _repo.AddOrUpdatePolicyAsync(new AgentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            AgentId = candidate.Id,
            PolicyJson = DefaultPolicyJson,
            CreatedAt = now
        }, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
