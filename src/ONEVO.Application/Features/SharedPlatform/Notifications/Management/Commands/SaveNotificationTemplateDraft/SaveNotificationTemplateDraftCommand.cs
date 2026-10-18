using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;

/// <summary>Saves the template's draft, creating one when none exists. <see cref="ExpectedRevision"/> must
/// match the draft being edited; drafts never affect live sends.</summary>
public sealed record SaveNotificationTemplateDraftCommand(Guid TemplateId, NotificationTemplateContentRequest Content, int? ExpectedRevision)
    : IRequest<Result<NotificationTemplateDetailResponse>>;
