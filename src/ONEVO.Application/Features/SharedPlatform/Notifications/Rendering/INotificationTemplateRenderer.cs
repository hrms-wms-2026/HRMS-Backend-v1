using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;

/// <summary>Final per-channel copy, ready to store on a delivery. Email HTML has CSS inlined and every
/// variable value encoded; empty channel fields fall back to the in-app copy.</summary>
public sealed record RenderedNotificationContent(
    string InAppTitle,
    string InAppBody,
    string EmailSubject,
    string EmailHtml,
    string EmailText,
    string TrayTitle,
    string TrayBody,
    string ActionKey);

public sealed record NotificationRenderResult(RenderedNotificationContent? Content, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Content is not null && Errors.Count == 0;

    public static NotificationRenderResult Fail(IReadOnlyList<string> errors) => new(null, errors);
}

/// <summary>
/// Validates and renders template content against its catalog event. Templates are data: they are never
/// compiled or evaluated as code. Preview and delivery both use <see cref="Render"/>, so what an admin
/// previews is exactly what is sent.
/// </summary>
public interface INotificationTemplateRenderer
{
    /// <summary>Publication checks: unknown variables or action keys, active/unsafe HTML, unsupported CSS.</summary>
    IReadOnlyList<string> Validate(NotificationTemplateContent content, NotificationEventDefinition definition);

    NotificationRenderResult Render(
        NotificationTemplateContent content,
        NotificationEventDefinition definition,
        IReadOnlyDictionary<string, string> variables);
}
