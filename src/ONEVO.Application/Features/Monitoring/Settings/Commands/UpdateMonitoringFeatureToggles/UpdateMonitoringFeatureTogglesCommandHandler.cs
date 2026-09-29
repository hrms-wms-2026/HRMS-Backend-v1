using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Settings.Mappers;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

public class UpdateMonitoringFeatureTogglesCommandHandler
    : IRequestHandler<UpdateMonitoringFeatureTogglesCommand, Result<MonitoringFeatureTogglesResponse>>
{
    private readonly IMonitoringFeatureTogglesRepository _toggles;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICacheService _cache;
    private readonly ITrayPolicyRefreshNotifier? _policyRefresh;

    public UpdateMonitoringFeatureTogglesCommandHandler(
        IMonitoringFeatureTogglesRepository toggles,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICacheService cache,
        ITrayPolicyRefreshNotifier? policyRefresh = null)
    {
        _toggles = toggles;
        _currentUser = currentUser;
        _clock = clock;
        _cache = cache;
        _policyRefresh = policyRefresh;
    }

    public async Task<Result<MonitoringFeatureTogglesResponse>> Handle(
        UpdateMonitoringFeatureTogglesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<MonitoringFeatureTogglesResponse>.Forbidden("Tenant context missing.");

        if (!_currentUser.HasPermission("monitoring:configure"))
            return Result<MonitoringFeatureTogglesResponse>.Forbidden(
                "You do not have permission to configure monitoring settings.");

        if (_currentUser.LegalEntityId is not Guid legalEntityId
            || !await _toggles.LegalEntityExistsAsync(tenantId, legalEntityId, ct))
            return Result<MonitoringFeatureTogglesResponse>.UnprocessableEntity(
                "Select an active company before configuring monitoring settings.");

        var now = _clock.UtcNow;
        var existing = await _toggles.GetByLegalEntityIdAsync(
            tenantId, legalEntityId, includeTenantFallback: false, ct);

        if (existing is not null)
        {
            existing.ActivityMonitoring = request.ActivityMonitoring;
            existing.ApplicationTracking = request.ApplicationTracking;
            existing.DocumentTracking = request.DocumentTracking;
            existing.CommunicationTracking = request.CommunicationTracking;
            existing.ScreenshotCapture = request.ScreenshotCapture;
            existing.AutoScreenshotCapture = request.AutoScreenshotCapture;
            existing.MeetingDetection = request.MeetingDetection;
            existing.DeviceTracking = request.DeviceTracking;
            existing.WorkLocationVerification = request.WorkLocationVerification;
            existing.IdentityVerification = request.IdentityVerification;
            existing.Biometric = request.Biometric;
            existing.IdleThresholdMinutes = request.IdleThresholdMinutes;
            existing.AllowedRadiusMeters = request.AllowedRadiusMeters;
            existing.UpdatedAt = now;
            _toggles.Update(existing);
        }
        else
        {
            existing = new MonitoringFeatureToggles
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                LegalEntityId = legalEntityId,
                ActivityMonitoring = request.ActivityMonitoring,
                ApplicationTracking = request.ApplicationTracking,
                DocumentTracking = request.DocumentTracking,
                CommunicationTracking = request.CommunicationTracking,
                ScreenshotCapture = request.ScreenshotCapture,
                AutoScreenshotCapture = request.AutoScreenshotCapture,
                MeetingDetection = request.MeetingDetection,
                DeviceTracking = request.DeviceTracking,
                WorkLocationVerification = request.WorkLocationVerification,
                IdentityVerification = request.IdentityVerification,
                Biometric = request.Biometric,
                IdleThresholdMinutes = request.IdleThresholdMinutes,
                AllowedRadiusMeters = request.AllowedRadiusMeters,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _toggles.AddAsync(existing, ct);
        }

        await _toggles.SaveChangesAsync(ct);

        await _cache.RemoveByPrefixAsync($"tenant:{tenantId}:monitoring-toggle:", ct);
        if (_policyRefresh is not null)
            await _policyRefresh.NotifyTenantAsync(tenantId, ct);

        return Result<MonitoringFeatureTogglesResponse>.Success(MonitoringFeatureTogglesMapper.ToResponse(existing));
    }
}
