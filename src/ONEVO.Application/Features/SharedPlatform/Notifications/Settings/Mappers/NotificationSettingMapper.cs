using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Mappers;

public static class NotificationSettingMapper
{
    /// <summary>Published templates only: a tenant can never select an unpublished draft.</summary>
    public static IReadOnlyList<NotificationTemplateOptionResponse> PublishedOptions(
        IEnumerable<NotificationTemplate> templates, string eventKey)
        => templates
            .Where(t => t.EventKey == eventKey && t.ActiveVersionId is not null)
            .OrderByDescending(t => t.Code == t.EventKey).ThenBy(t => t.Name)
            .Select(t => new NotificationTemplateOptionResponse(t.Id, t.Name, t.Code == t.EventKey))
            .ToList();

    public static NotificationSettingResponse ToSetting(
        NotificationEventDefinition definition, TenantNotificationRule? rule, IEnumerable<NotificationTemplate> templates)
    {
        var effective = EffectiveNotificationRule.Resolve(definition, rule);
        return new NotificationSettingResponse(
            definition.Key, definition.Module, definition.DisplayName,
            NotificationChannelNames.ToNames(definition.SupportedChannels),
            NotificationChannelNames.ToNames(definition.RequiredChannels),
            NotificationChannelNames.ToNames(definition.DefaultChannels | definition.RequiredChannels),
            NotificationChannelNames.ToNames(effective.Channels),
            effective.TemplateId,
            effective.RecipientRule,
            definition.AllowedRecipientRules,
            PublishedOptions(templates, definition.Key),
            effective.IsOverridden,
            effective.Revision);
    }
}
