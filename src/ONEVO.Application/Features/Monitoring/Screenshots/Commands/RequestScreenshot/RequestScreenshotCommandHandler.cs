using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Screenshots.Mappers;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.RequestScreenshot;

public class RequestScreenshotCommandHandler
    : IRequestHandler<RequestScreenshotCommand, Result<AgentCommandDto>>
{
    private static readonly TimeSpan CommandTtl = TimeSpan.FromMinutes(5);

    private readonly IAgentCommandRepository _commands;
    private readonly ITrayActivationRepository _trayRepo;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ICurrentUser _currentUser;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IMonitoringPermissionConsentRepository _consents;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RequestScreenshotCommandHandler> _logger;

    public RequestScreenshotCommandHandler(
        IAgentCommandRepository commands,
        ITrayActivationRepository trayRepo,
        IMonitoringToggleResolver toggleResolver,
        ICurrentUser currentUser,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IMonitoringPermissionConsentRepository consents,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<RequestScreenshotCommandHandler> logger)
    {
        _commands = commands;
        _trayRepo = trayRepo;
        _toggleResolver = toggleResolver;
        _currentUser = currentUser;
        _employeeIdentity = employeeIdentity;
        _consents = consents;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<AgentCommandDto>> Handle(
        RequestScreenshotCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;

        var device = await _trayRepo.FindActiveDeviceAsync(
            request.AgentDeviceId, tenantId, cancellationToken);

        if (device is null)
            return Result<AgentCommandDto>.Failure(MonitoringErrors.AgentDeviceNotFound, 404);

        // Phase 1: tray JWT binds UserId; use that as employeeId for toggle resolution
        var employeeId = device.UserId;

        var enabled = await _toggleResolver.IsEnabledAsync(
            tenantId,
            employeeId,
            MonitoringCapability.ScreenshotCapture,
            cancellationToken);

        if (!enabled)
            return Result<AgentCommandDto>.Failure(MonitoringErrors.ScreenshotCapabilityDisabled, 403);

        // Consent gate: the company toggle alone is not enough - the employee must have allowed
        // screen monitoring on the tray's Privacy & Transparency screen. Consent rows are keyed
        // by the resolved Employee.Id for the device's legal entity, the same key
        // RecordPermissionConsentCommandHandler writes them under. No row, or a row with
        // ScreenMonitoring = false, means no screenshot command is ever created.
        var consentEmployeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, device.UserId, device.LegalEntityId, cancellationToken);
        var consent = await _consents.GetLatestAsync(tenantId, consentEmployeeId, cancellationToken);

        if (consent is null || !consent.ScreenMonitoring)
        {
            _logger.LogWarning(
                "Screenshot request rejected - no screen-monitoring consent. TenantId={TenantId} DeviceId={DeviceId}",
                tenantId, request.AgentDeviceId);
            return Result<AgentCommandDto>.Failure(
                MonitoringErrors.ScreenshotConsentMissing, 403, MonitoringErrors.ScreenshotConsentMissingCode);
        }

        var now = _clock.UtcNow;
        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentDeviceId = request.AgentDeviceId,
            RequestedById = _currentUser.UserId,
            CommandType = "capture_screenshot",
            PayloadJson = "{}",
            Status = "pending",
            ExpiresAt = now.Add(CommandTtl),
            CreatedAt = now
        };

        _commands.Add(command);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Screenshot command created. TenantId={TenantId} DeviceId={DeviceId} CommandId={CommandId}",
            tenantId, request.AgentDeviceId, command.Id);

        return Result<AgentCommandDto>.Success(AgentCommandMapper.ToDto(command));
    }
}
