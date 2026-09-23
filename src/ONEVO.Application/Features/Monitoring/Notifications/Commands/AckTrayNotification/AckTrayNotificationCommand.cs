using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Notifications.Commands.AckTrayNotification;

public record AckTrayNotificationCommand(Guid NotificationId) : IRequest<Result>;
