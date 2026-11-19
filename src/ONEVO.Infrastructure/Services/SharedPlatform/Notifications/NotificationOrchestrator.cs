using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <inheritdoc cref="INotificationOrchestrator"/>
public sealed class NotificationOrchestrator : INotificationOrchestrator
{
    /// <summary>A tray alert not collected within this window is marked Expired rather than shown late.</summary>
    public static readonly TimeSpan TrayExpiry = TimeSpan.FromHours(24);

    private readonly ITenantNotificationRuleRepository _rules;
    private readonly INotificationTemplateStore _templates;
    private readonly INotificationDeliveryRepository _deliveries;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<NotificationOrchestrator> _logger;
    private readonly INotificationRepository _inbox;

    public NotificationOrchestrator(
        ITenantNotificationRuleRepository rules, INotificationTemplateStore templates,
        INotificationDeliveryRepository deliveries, INotificationTemplateRenderer renderer,
        IOutboxWriter outbox, IDateTimeProvider clock, ILogger<NotificationOrchestrator> logger,
        INotificationRepository inbox)
    {
        _inbox = inbox;
        _rules = rules;
        _templates = templates;
        _deliveries = deliveries;
        _renderer = renderer;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task EnqueueAsync(NotificationDispatchRequest request, CancellationToken ct = default)
    {
        var definition = NotificationEventCatalog.Get(request.EventKey);
        var rule = EffectiveNotificationRule.Resolve(definition, await _rules.GetAsync(request.TenantId, request.EventKey, ct));
        if (rule.Channels == NotificationChannels.None)
            return;

        var (template, version, failure) = await ResolveVersionAsync(definition, rule.TemplateId, ct);
        RenderedNotificationContent? content = null;
        if (version is not null)
        {
            var rendered = _renderer.Render(version.ToContent(), definition, request.Variables);
            if (rendered.IsSuccess)
                content = rendered.Content;
            else
                failure = "render_failed";
        }
        if (failure is not null)
        {
            // Variable names only - values may carry personal data.
            _logger.LogError(
                "Notification {EventKey} event {EventId} for tenant {TenantId} could not be prepared: {Reason}.",
                request.EventKey, request.EventId, request.TenantId, failure);
        }

        var now = _clock.UtcNow;
        foreach (var channel in NotificationChannelNames.Each(rule.Channels))
        {
            if (await _deliveries.ExistsAsync(request.TenantId, request.EventId, request.RecipientUserId, channel, ct))
                continue;

            var delivery = new NotificationDelivery
            {
                Id = Guid.NewGuid(), TenantId = request.TenantId, EventId = request.EventId, EventKey = request.EventKey,
                RecipientUserId = request.RecipientUserId, Channel = channel, TemplateVersionId = version?.Id,
                RelatedEntityType = request.RelatedEntityType, RelatedEntityId = request.RelatedEntityId,
                ActionKey = content?.ActionKey ?? NotificationActionKeys.None, CreatedAt = now,
                ExpiresAt = channel == NotificationChannels.Tray ? now.Add(TrayExpiry) : null
            };

            if (content is null)
            {
                delivery.Title = definition.DisplayName;
                delivery.Status = NotificationDeliveryStatus.Failed;
                delivery.StatusReason = failure;
                delivery.CompletedAt = now;
                await _deliveries.AddAsync(delivery, ct);
                continue;
            }

            (delivery.Title, delivery.Body) = channel == NotificationChannels.Tray
                ? (content.TrayTitle, content.TrayBody)
                : (content.InAppTitle, content.InAppBody);
            if (channel == NotificationChannels.Email)
            {
                delivery.EmailSubject = content.EmailSubject;
                delivery.EmailHtml = content.EmailHtml;
                delivery.EmailText = content.EmailText;
            }

            if (channel == NotificationChannels.InApp)
            {
                // A database write, not an external side effect: written in the producer's own transaction, exactly
                // when the legacy dispatcher wrote it, so it is never late and never ahead of an inbox refresh.
                if (template is { InAppEnabled: false })
                {
                    delivery.Status = NotificationDeliveryStatus.Skipped;
                    delivery.StatusReason = "in_app_disabled";
                    delivery.CompletedAt = now;
                }
                else
                {
                    var row = new Notification
                    {
                        Id = Guid.NewGuid(), TenantId = request.TenantId, RecipientUserId = request.RecipientUserId,
                        TemplateCode = request.EventKey, Title = delivery.Title, Body = delivery.Body,
                        RelatedEntityType = request.RelatedEntityType, RelatedEntityId = request.RelatedEntityId,
                        IsRead = false, CreatedAt = now
                    };
                    await _inbox.AddAsync(row, ct);
                    delivery.NotificationId = row.Id;
                    delivery.Status = NotificationDeliveryStatus.AvailableInInbox;
                    delivery.AttemptCount = 1;
                    delivery.CompletedAt = now;
                }
                await _deliveries.AddAsync(delivery, ct);
                continue;
            }

            await _deliveries.AddAsync(delivery, ct);
            // Email is delivered through the outbox. The tray collects its own pending deliveries;
            // it only gets a post-commit wake-up so it fetches now rather than on its slow fallback poll.
            if (channel == NotificationChannels.Tray)
                await _outbox.EnqueueAsync(OutboxMessageTypes.NotificationTrayPush,
                    new NotificationTrayPushPayload(delivery.TenantId, delivery.RecipientUserId), delivery.TenantId, ct);
            else
                await _outbox.EnqueueAsync(OutboxMessageTypes.NotificationDelivery,
                    new NotificationDeliveryPayload(delivery.Id, delivery.TenantId), delivery.TenantId, ct);
        }
    }

    private async Task<(NotificationTemplate? Template, NotificationTemplateVersion? Version, string? Failure)> ResolveVersionAsync(
        NotificationEventDefinition definition, Guid? selectedTemplateId, CancellationToken ct)
    {
        NotificationTemplate? template = null;
        if (selectedTemplateId is { } templateId)
        {
            var selected = await _templates.GetTrackedAsync(templateId, ct);
            if (selected is not null && selected.EventKey == definition.Key && selected.ActiveVersionId is not null)
                template = selected;
        }
        // A selected template that was since unpublished or removed falls back to the product default.
        template ??= await _templates.GetDefaultForEventAsync(definition.Key, ct);
        if (template?.ActiveVersionId is not { } versionId)
            return (template, null, "no_published_template");

        var version = await _templates.GetVersionAsync(versionId, ct);
        return version is null ? (template, null, "no_published_template") : (template, version, null);
    }
}
