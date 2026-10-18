using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Mappers;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;

public sealed class UpdateNotificationSettingCommandHandler
    : IRequestHandler<UpdateNotificationSettingCommand, Result<NotificationSettingResponse>>
{
    public const string StaleSetting = "This notification setting was changed by someone else. Reload to review the latest values.";

    private readonly ICurrentUser _currentUser;
    private readonly ITenantNotificationRuleRepository _rules;
    private readonly INotificationTemplateStore _templates;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateNotificationSettingCommandHandler(
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

    public async Task<Result<NotificationSettingResponse>> Handle(UpdateNotificationSettingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<NotificationSettingResponse>.Forbidden("Authentication required.");
        if (!NotificationEventCatalog.TryGet(request.EventKey, out var definition))
            return Result<NotificationSettingResponse>.NotFound("Notification event not found.");

        if (!NotificationChannelNames.TryParse(request.EnabledChannels, out var channels, out var unknown))
            return Result<NotificationSettingResponse>.Failure($"Unknown channel '{unknown}'.");
        var unsupported = channels & ~definition.SupportedChannels;
        if (unsupported != NotificationChannels.None)
            return Result<NotificationSettingResponse>.Failure(
                $"{definition.DisplayName} does not support: {string.Join(", ", NotificationChannelNames.ToNames(unsupported))}.");
        var missingRequired = definition.RequiredChannels & ~channels;
        if (missingRequired != NotificationChannels.None)
            return Result<NotificationSettingResponse>.Failure(
                $"{string.Join(", ", NotificationChannelNames.ToNames(missingRequired))} is required for {definition.DisplayName} and cannot be turned off.");

        var recipientRule = string.IsNullOrWhiteSpace(request.RecipientRule) ? NotificationRecipientRules.Default : request.RecipientRule;
        if (!definition.AllowedRecipientRules.Contains(recipientRule, StringComparer.Ordinal))
            return Result<NotificationSettingResponse>.Failure($"Recipient rule '{recipientRule}' is not available for this event.");

        var templates = await _templates.ListAsync(definition.Key, ct);
        if (request.TemplateId is { } templateId
            && !templates.Any(t => t.Id == templateId && t.EventKey == definition.Key && t.ActiveVersionId is not null))
            return Result<NotificationSettingResponse>.Failure("The selected template is not a published template for this event.");

        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;
        var rule = await _rules.GetTrackedAsync(tenantId, definition.Key, ct);
        if (rule is null)
        {
            if (request.ExpectedRevision is not null)
                return Result<NotificationSettingResponse>.Conflict(StaleSetting);
            rule = new TenantNotificationRule
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventKey = definition.Key, Revision = 1, CreatedAt = now
            };
            await _rules.AddAsync(rule, ct);
        }
        else if (request.ExpectedRevision != rule.Revision)
        {
            return Result<NotificationSettingResponse>.Conflict(StaleSetting);
        }
        else
        {
            rule.Revision++;
        }

        rule.EnabledChannels = channels;
        rule.TemplateId = request.TemplateId;
        rule.RecipientRule = recipientRule;
        rule.UpdatedByUserId = _currentUser.UserId;
        rule.UpdatedAt = now;

        await _auditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = _currentUser.UserId,
            Action = "notification_settings.updated", ResourceType = "NotificationEvent",
            NewValuesJson = JsonSerializer.Serialize(new
            {
                eventKey = definition.Key, channels = NotificationChannelNames.ToNames(channels),
                templateId = request.TemplateId, recipientRule, revision = rule.Revision
            }),
            CreatedAt = now
        }, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<NotificationSettingResponse>.Conflict(StaleSetting);
        }

        return Result<NotificationSettingResponse>.Success(NotificationSettingMapper.ToSetting(definition, rule, templates));
    }
}
