using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkNotificationRead;

public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest<Result>;
