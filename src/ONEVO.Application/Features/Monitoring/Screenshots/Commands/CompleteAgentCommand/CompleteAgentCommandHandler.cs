using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;

public class CompleteAgentCommandHandler : IRequestHandler<CompleteAgentCommandCommand, Result>
{
    private readonly IAgentCommandRepository _commands;
    private readonly IEvidenceAssetRepository _assets;
    private readonly ITrayActivationRepository _trayRepo;
    private readonly ITrayCurrentDevice _device;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CompleteAgentCommandHandler> _logger;

    public CompleteAgentCommandHandler(
        IAgentCommandRepository commands,
        IEvidenceAssetRepository assets,
        ITrayActivationRepository trayRepo,
        ITrayCurrentDevice device,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<CompleteAgentCommandHandler> logger)
    {
        _commands = commands;
        _assets = assets;
        _trayRepo = trayRepo;
        _device = device;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(CompleteAgentCommandCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _device.TenantId;
        var deviceId = _device.DeviceRegistrationId;

        var command = await _commands.GetByIdAsync(tenantId, request.CommandId, cancellationToken);

        if (command is null)
            return Result.Failure(MonitoringErrors.AgentCommandNotFound, 404);

        if (command.AgentDeviceId != deviceId)
            return Result.Failure(MonitoringErrors.AgentCommandDeviceMismatch, 403);

        // Natural idempotency: already completed → agent can safely retry on network failure
        if (command.Status == "completed")
        {
            _logger.LogInformation(
                "CompleteAgentCommand replayed for already-completed command. CommandId={CommandId}",
                request.CommandId);
            return Result.Success();
        }

        if (command.Status is "failed" or "expired")
            return Result.Failure(MonitoringErrors.AgentCommandAlreadySettled, 409);

        var now = _clock.UtcNow;

        if (command.ExpiresAt <= now)
            return Result.Failure(MonitoringErrors.AgentCommandExpired, 410);

        if (request.Success && request.FileRecordId.HasValue)
        {
            // Phase 1: UserId on TrayDeviceRegistration serves as employeeId
            var registeredDevice = await _trayRepo.FindActiveDeviceAsync(deviceId, tenantId, cancellationToken);
            var employeeId = registeredDevice?.UserId ?? _device.UserId;

            var asset = new MonitoringEvidenceAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                AgentDeviceId = deviceId,
                AgentCommandId = request.CommandId,
                FileRecordId = request.FileRecordId.Value,
                EvidenceType = "screenshot",
                Source = "agent",
                TriggerType = "on_demand",
                CapturedAt = request.CapturedAt,
                CreatedAt = now
            };

            _assets.Add(asset);
            command.Status = "completed";
            command.CompletedAt = now;
            command.ResultJson = request.ResultJson;
        }
        else
        {
            command.Status = "failed";
            command.CompletedAt = now;
            command.ResultJson = request.ResultJson;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Agent command completed. CommandId={CommandId} Success={Success}",
            request.CommandId, request.Success);

        return Result.Success();
    }
}
