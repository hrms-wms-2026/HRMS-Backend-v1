using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Mappers;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;

public sealed class ResetNotificationSettingCommandHandler
    : IRequestHandler<ResetNotificationSettingCommand, Result<NotificationSettingResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantNotificationRuleRepository _rules;
    private readonly INotificationTemplateStore _templates;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ResetNotificationSettingCommandHandler(
        ICurrentUser currentUser, ITenantNotificationRuleRepository rules, INotificationTemplateStore templates,
        IAuditLogRepository auditLogs, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _rules = rules;
        _templates = templates;
        _auditLogs = auditLogs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<NotificationSettingResponse>> Handle(ResetNotificationSettingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<NotificationSettingResponse>.Forbidden("Authentication required.");
        if (!NotificationEventCatalog.TryGet(request.EventKey, out var definition))
            return Result<NotificationSettingResponse>.NotFound("Notification event not found.");

        var rule = await _rules.GetTrackedAsync(_currentUser.TenantId, definition.Key, ct);
        if (rule is not null)
        {
            _rules.Remove(rule);
            await _auditLogs.AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(), TenantId = _currentUser.TenantId, UserId = _currentUser.UserId,
                Action = "notification_settings.reset", ResourceType = "NotificationEvent",
                OldValuesJson = JsonSerializer.Serialize(new { eventKey = definition.Key, revision = rule.Revision }),
                CreatedAt = _clock.UtcNow
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var templates = await _templates.ListAsync(definition.Key, ct);
        return Result<NotificationSettingResponse>.Success(NotificationSettingMapper.ToSetting(definition, null, templates));
    }
}
