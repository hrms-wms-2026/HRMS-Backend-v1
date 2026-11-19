using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;

/// <summary>A variable a template for this event may reference as <c>{{Key}}</c>.</summary>
public sealed record NotificationVariableDefinition(string Key, string Type = "string", bool Required = false);

/// <summary>
/// A notification event registered in code. Templates bind to one event; tenants route events to
/// channels. Admins cannot invent events - the trigger, variables, recipients and actions live here.
/// </summary>
/// <param name="OrchestratorOwned">True once this event's producer goes through INotificationOrchestrator
/// (per-event cutover). While false the legacy dispatcher remains the single owner of in-app delivery.</param>
public sealed record NotificationEventDefinition(
    string Key,
    string Module,
    string DisplayName,
    NotificationChannels SupportedChannels,
    NotificationChannels DefaultChannels,
    NotificationChannels RequiredChannels,
    IReadOnlyList<NotificationVariableDefinition> AllowedVariables,
    IReadOnlyList<string> AllowedRecipientRules,
    IReadOnlyList<string> AllowedActionKeys,
    bool OrchestratorOwned = false)
{
    public bool Supports(NotificationChannels channels) => (channels & ~SupportedChannels) == NotificationChannels.None;
}

public static class NotificationRecipientRules
{
    /// <summary>Exactly the recipients the producer resolves today (assignee, approver, requester...).</summary>
    public const string Default = "default";
}

public static class NotificationActionKeys
{
    /// <summary>Open the related record through the existing inbox navigation.</summary>
    public const string OpenRelated = "open_related";
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = [OpenRelated, None];
}
