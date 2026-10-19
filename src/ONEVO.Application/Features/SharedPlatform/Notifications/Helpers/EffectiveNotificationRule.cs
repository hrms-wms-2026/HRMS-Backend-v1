using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;

/// <summary>
/// How an event is actually delivered for a tenant: the saved override if there is one, otherwise the
/// catalog defaults. Required channels are always on and unsupported channels always off, whatever is stored.
/// </summary>
public sealed record EffectiveNotificationRule(
    string EventKey,
    NotificationChannels Channels,
    Guid? TemplateId,
    string RecipientRule,
    bool IsOverridden,
    int? Revision)
{
    public static EffectiveNotificationRule Resolve(NotificationEventDefinition definition, TenantNotificationRule? rule)
    {
        if (rule is null)
            return new(definition.Key, definition.DefaultChannels | definition.RequiredChannels,
                TemplateId: null, NotificationRecipientRules.Default, IsOverridden: false, Revision: null);

        var channels = (rule.EnabledChannels | definition.RequiredChannels) & definition.SupportedChannels;
        var recipientRule = definition.AllowedRecipientRules.Contains(rule.RecipientRule, StringComparer.Ordinal)
            ? rule.RecipientRule
            : NotificationRecipientRules.Default;
        return new(definition.Key, channels, rule.TemplateId, recipientRule, IsOverridden: true, rule.Revision);
    }
}
