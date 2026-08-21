using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Notifications.Queries.GetPendingTrayNotifications;

public record GetPendingTrayNotificationsQuery : IRequest<Result<List<TrayNotificationDto>>>;
