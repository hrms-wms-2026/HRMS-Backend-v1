namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

public sealed record NotificationVariableResponse(string Key, string Type, bool Required);

public sealed record NotificationEventResponse(
    string Key,
    string Module,
    string DisplayName,
    IReadOnlyList<string> SupportedChannels,
    IReadOnlyList<string> DefaultChannels,
    IReadOnlyList<string> RequiredChannels,
    IReadOnlyList<NotificationVariableResponse> Variables,
    IReadOnlyList<string> RecipientRules,
    IReadOnlyList<string> ActionKeys,
    /// <summary>True when the event is delivered by the orchestrator, so alternate templates and non-in-app channel
    /// copy are actually used. Built-in (legacy) events only use the default template's in-app copy.</summary>
    bool Customizable);

public sealed record NotificationTemplateContentRequest(
    string InAppTitle,
    string InAppBody,
    string? EmailSubject,
    string? EmailHtml,
    string? EmailCss,
    string? EmailText,
    string? TrayTitle,
    string? TrayBody,
    string ActionKey);

public sealed record NotificationTemplateSummaryResponse(
    Guid Id,
    string Code,
    string EventKey,
    string Name,
    string Locale,
    bool IsProductDefault,
    int? ActiveVersion,
    bool HasDraft,
    DateTimeOffset? UpdatedAt);

public sealed record NotificationTemplateVersionResponse(
    Guid Id,
    int Version,
    string Status,
    int Revision,
    string InAppTitle,
    string InAppBody,
    string? EmailSubject,
    string? EmailHtml,
    string? EmailCss,
    string? EmailText,
    string? TrayTitle,
    string? TrayBody,
    string ActionKey,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PublishedAt,
    Guid? PublishedByUserId);

public sealed record NotificationTemplateVersionSummaryResponse(
    Guid Id, int Version, string Status, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, Guid? PublishedByUserId);

public sealed record NotificationTemplateDetailResponse(
    Guid Id,
    string Code,
    string EventKey,
    string Name,
    string Locale,
    bool IsProductDefault,
    NotificationTemplateVersionResponse? ActiveVersion,
    NotificationTemplateVersionResponse? Draft,
    IReadOnlyList<NotificationTemplateVersionSummaryResponse> Versions);

public sealed record RenderedNotificationResponse(
    string InAppTitle,
    string InAppBody,
    string EmailSubject,
    string EmailHtml,
    string EmailText,
    string TrayTitle,
    string TrayBody,
    string ActionKey);

public sealed record NotificationTemplatePreviewResponse(
    bool IsValid,
    IReadOnlyList<string> Errors,
    RenderedNotificationResponse? Rendered,
    IReadOnlyDictionary<string, string> SampleVariables);

public sealed record NotificationTestSendResponse(string Channel, string Recipient, bool Accepted, string? Error);

public sealed record CreateNotificationTemplateRequest(string EventKey, string Name, NotificationTemplateContentRequest Content);

public sealed record SaveNotificationTemplateDraftRequest(NotificationTemplateContentRequest Content, int? Revision);

public sealed record PreviewNotificationTemplateRequest(NotificationTemplateContentRequest? Content, Dictionary<string, string>? Variables);

public sealed record PublishNotificationTemplateRequest(int Revision);

public sealed record SendTestNotificationRequest(string? Channel, Dictionary<string, string>? Variables);
