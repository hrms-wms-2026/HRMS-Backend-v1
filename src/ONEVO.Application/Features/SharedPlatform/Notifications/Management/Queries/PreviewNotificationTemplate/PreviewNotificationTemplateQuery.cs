using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.PreviewNotificationTemplate;

/// <summary>Renders <paramref name="Content"/> (unsaved editor content) or, when null, the draft / active
/// version - with sample variables. Uses the same renderer as delivery.</summary>
public sealed record PreviewNotificationTemplateQuery(
    Guid TemplateId,
    NotificationTemplateContentRequest? Content,
    IReadOnlyDictionary<string, string>? Variables)
    : IRequest<Result<NotificationTemplatePreviewResponse>>;
