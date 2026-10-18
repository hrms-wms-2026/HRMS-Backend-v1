using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;

/// <summary>Creates an additional template for a registered event. It starts as an unpublished draft.</summary>
public sealed record CreateNotificationTemplateCommand(string EventKey, string Name, NotificationTemplateContentRequest Content)
    : IRequest<Result<NotificationTemplateDetailResponse>>;
