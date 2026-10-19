using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.GetNotificationTemplateDetail;

public sealed record GetNotificationTemplateDetailQuery(Guid TemplateId) : IRequest<Result<NotificationTemplateDetailResponse>>;
