using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.RestoreNotificationTemplateVersion;

/// <summary>Copies a published version into a new draft. Nothing goes live until that draft is published.</summary>
public sealed record RestoreNotificationTemplateVersionCommand(Guid TemplateId, Guid VersionId)
    : IRequest<Result<NotificationTemplateDetailResponse>>;
