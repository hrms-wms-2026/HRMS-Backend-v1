namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;

public sealed record NotificationTemplateOptionResponse(Guid Id, string Name, bool IsProductDefault);

public sealed record NotificationSettingResponse(
    string EventKey,
    string Module,
    string DisplayName,
    IReadOnlyList<string> SupportedChannels,
    IReadOnlyList<string> RequiredChannels,
    IReadOnlyList<string> DefaultChannels,
    IReadOnlyList<string> EnabledChannels,
    Guid? TemplateId,
    string RecipientRule,
    IReadOnlyList<string> RecipientRules,
    IReadOnlyList<NotificationTemplateOptionResponse> Templates,
    bool IsOverridden,
    int? Revision);

public sealed record UpdateNotificationSettingRequest(
    IReadOnlyList<string> EnabledChannels,
    Guid? TemplateId,
    string? RecipientRule,
    int? Revision);
